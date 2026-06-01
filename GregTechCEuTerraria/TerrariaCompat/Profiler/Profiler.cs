#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GregTechCEuTerraria.TerrariaCompat.Profiler;

// Lightweight in-process profiler. Three counter kinds:
//   - Counter - monotonic delta; UI shows events/sec averaged over the window.
//   - Gauge   - instantaneous reading; UI shows current + min/max over the window.
//   - Timer   - like Counter but in ticks; UI shows ms/sec (= % of frame time).
//
// Sampling cadence: ProfilerSystem.PostUpdateEverything pushes one sample per
// 6 frames into a ring buffer (10 Hz, 1800 samples = 3 min window). All counters
// share the same sampling clock so the graph X-axes are comparable.
//
// No locks, no Interlocked. tML is single-threaded outside FastParallel; we
// never increment from a parallel worker. (If we ever need to, the call site
// must marshal back to the main thread before touching the counter.)
public enum ProfilerKind : byte { Counter, Gauge, Timer }

public sealed class ProfilerCounter
{
	public readonly string Category;
	public readonly string Name;
	public readonly ProfilerKind Kind;

	// Raw value:
	//   Counter - total events since registration (monotonic).
	//   Gauge   - last set value.
	//   Timer   - total Stopwatch ticks since registration (monotonic).
	public long ValueRaw;

	// Sample ring (last N samples; oldest at SampleHead, newest at SampleHead-1).
	public readonly double[] Samples;
	public int SampleHead;
	public long LastSnapshotValue;

	// When true, this counter's samples are written externally (e.g. by
	// ProfilerSyncPacket pushing server-side values into client-side
	// `server.*` mirrors) and the local SampleAll should NOT touch it.
	// Without this flag the local sampler interleaves zero deltas between
	// the wire's real samples, producing the spurious `real, 0, real, 0, ...`
	// alternation in graphs and dumps.
	public bool ExternallySampled;

	// Delta-sync bookkeeping (server-side): the last (ValueRaw, newestSample)
	// shipped by ProfilerSyncPacket, so unchanged counters are skipped. Most
	// counters are static between broadcasts (per-machine mem gauges, idle
	// machine timers), so shipping all 1000+ every 6 ticks was the top
	// client-receive bandwidth. Server-only memo - not persisted, not synced.
	public long   SyncLastVal;
	public double SyncLastSamp;
	public bool   SyncInit;

	// Wire id for the interned sync format. Equals the counter's index in
	// Profiler._ordered (stable + append-only within an epoch). The string
	// Category/Name ship ONCE per counter per epoch (a "def" row keyed by this
	// id); every subsequent broadcast ships only compact (id, raw, sample)
	// value rows. A 50-byte string name (e.g. machine_systemtick.by_type.X)
	// shipping every broadcast was the bulk of the profiler's own bandwidth.
	public ushort SyncId;
	// Server-side: true once this counter's def row (id->category/name/kind) has
	// been shipped. Reset to false on epoch bump (Profiler.Reset) AND periodically
	// by ProfilerSyncPacket's def-resync, so a client that missed the one-shot
	// original (host-and-play world load, late join) relearns the name. Value
	// rows are self-describing (carry their kind), so they ship independently of
	// this flag - a value for a not-yet-learned id is dropped, never desynced.
	public bool   SyncDefSent;

	internal ProfilerCounter(string category, string name, ProfilerKind kind, int windowSamples)
	{
		Category = category;
		Name     = name;
		Kind     = kind;
		Samples  = new double[windowSamples];
	}

	// Window summary in display units. Counter/Timer -> per-sec rate;
	// Gauge -> current value. Min/Max scan the ring.
	public (double current, double min, double max, double avg) Summarize()
	{
		double current, min = double.MaxValue, max = double.MinValue, sum = 0;
		int n = Samples.Length;
		for (int i = 0; i < n; i++)
		{
			double v = Samples[i];
			if (v < min) min = v;
			if (v > max) max = v;
			sum += v;
		}
		double avg = sum / n;
		// "Current" displays whatever the most recent sample is for Counter/Timer
		// (a per-window rate); for Gauge it's literally the live value.
		int newestIdx = (SampleHead - 1 + n) % n;
		current = Kind == ProfilerKind.Gauge ? ValueRaw : Samples[newestIdx];
		if (min == double.MaxValue) min = 0;
		if (max == double.MinValue) max = 0;
		return (current, min, max, avg);
	}
}

public static class Profiler
{
	public const int WindowSamples = 1800;        // 3 min at 10 Hz (180 s)
	public const int SamplePeriodFrames = 6;      // sample every 6 ticks (10 Hz)

	// Master gate (mirrored from GTConfig.EnableProfiler via OnChanged). When
	// false every Count/Gauge/Time/AccumulateTimer is a cheap no-op, the sample
	// loop + MP sync are skipped, and the UI button hides. Defaults true so
	// instrumentation works before the config loads.
	public static bool Enabled = true;

	// Insertion-ordered for stable UI listing.
	private static readonly Dictionary<string, ProfilerCounter> _counters = new();
	private static readonly List<ProfilerCounter> _ordered = new();

	public static IReadOnlyList<ProfilerCounter> All => _ordered;

	// Bumped on Reset (world load/unload). The interned sync format keys
	// counters by SyncId (= index in _ordered); after a reset those ids are
	// reused for different counters, so the client must drop its id->counter map
	// when the epoch changes. A byte is plenty - only equality matters.
	public static byte SyncEpoch { get; private set; }

	public static ProfilerCounter GetOrCreate(string category, string name, ProfilerKind kind)
	{
		string key = category + "." + name;
		if (_counters.TryGetValue(key, out var c)) return c;
		c = new ProfilerCounter(category, name, kind, WindowSamples) { SyncId = (ushort)_ordered.Count };
		_counters[key] = c;
		_ordered.Add(c);
		return c;
	}

	// -- Counters (monotonic event counters) ------------------------------
	public static void Count(string category, string name, long n = 1)
	{
		if (!Enabled) return;
		var c = GetOrCreate(category, name, ProfilerKind.Counter);
		c.ValueRaw += n;
	}

	// -- Gauges (instantaneous reading) -----------------------------------
	public static void Gauge(string category, string name, long value)
	{
		if (!Enabled) return;
		var c = GetOrCreate(category, name, ProfilerKind.Gauge);
		c.ValueRaw = value;
	}

	public static void Gauge(string category, string name, int value) => Gauge(category, name, (long)value);

	// -- Timers (Stopwatch-tick accumulator) ------------------------------
	public readonly struct TimerScope : IDisposable
	{
		private readonly ProfilerCounter? _c;
		private readonly long _start;
		internal TimerScope(ProfilerCounter? c) { _c = c; _start = c == null ? 0 : Stopwatch.GetTimestamp(); }
		public void Dispose() { if (_c != null) _c.ValueRaw += Stopwatch.GetTimestamp() - _start; }
	}
	// Disabled -> a null-counter scope (Dispose no-ops), so `using` call-sites
	// stay valid without paying the Stopwatch / dictionary cost.
	public static TimerScope Time(string category, string name) =>
		Enabled ? new(GetOrCreate(category, name, ProfilerKind.Timer)) : new((ProfilerCounter?)null);

	// Manual Stopwatch-tick accumulation - for hot loops that want to bucket
	// elapsed time by a runtime-derived key (e.g. machine type name) without
	// paying the GetOrCreate dictionary lookup AND the TimerScope ctor on every
	// iteration. Caller measures with Stopwatch.GetTimestamp() deltas, then
	// adds them into the named counter.
	public static void AccumulateTimer(string category, string name, long stopwatchTicks)
	{
		if (!Enabled) return;
		var c = GetOrCreate(category, name, ProfilerKind.Timer);
		c.ValueRaw += stopwatchTicks;
	}

	// -- Per-tick sample push (called from ProfilerSystem) ----------------
	internal static void SampleAll()
	{
		double windowSeconds = SamplePeriodFrames / 60.0;  // assumes 60 FPS reference
		foreach (var c in _ordered)
		{
			if (c.ExternallySampled) continue;  // server-mirrored counter; wire owns the ring
			double sample;
			switch (c.Kind)
			{
				case ProfilerKind.Counter:
					sample = (c.ValueRaw - c.LastSnapshotValue) / windowSeconds;
					c.LastSnapshotValue = c.ValueRaw;
					break;
				case ProfilerKind.Timer:
					// Convert Stopwatch ticks to milliseconds per second of wall time.
					double deltaMs = (c.ValueRaw - c.LastSnapshotValue) * 1000.0 / Stopwatch.Frequency;
					sample = deltaMs / windowSeconds;  // ms of work per second of wall time
					c.LastSnapshotValue = c.ValueRaw;
					break;
				default: // Gauge
					sample = c.ValueRaw;
					break;
			}
			c.Samples[c.SampleHead] = sample;
			c.SampleHead = (c.SampleHead + 1) % c.Samples.Length;
		}
	}

	internal static void Reset()
	{
		_counters.Clear();
		_ordered.Clear();
		_spikes.Clear();
		_sampleIndex = 0;
		// New epoch: SyncIds are reused for fresh counters, so existing clients
		// must rebuild their id map (they key off this byte changing).
		SyncEpoch++;
	}

	// -- Spike log --------------------------------------------------------
	// When a sample's aggregate frame budget exceeds SpikeThresholdMs, we
	// snapshot the per-timer breakdown for that sample into this ring. Lets
	// you trace "what was the server doing during this 1.2s stall" without
	// having to cross-reference 50+ sample arrays manually.
	public sealed class SpikeRecord
	{
		public long   SampleIndex;
		public double FrameBudgetMs;
		public long   HeapMb;
		public int    ActiveMachines;
		public int    Gc0Delta, Gc1Delta, Gc2Delta;
		public long   AllocMbPerSec;
		// (counterName, msDelta) for top-N timers in the spike window.
		public List<(string name, double ms)> TopTimers = new();
	}

	// Lowered from 500 -> 100 ms/sec after the 2026-05-31 spike capture: an
	// FPS=1 frame only registered 211 ms of timed work (network handlers were
	// uninstrumented), well under 500, so the spike log stayed empty. 100 ms/s
	// = ~10% frame budget - coarse enough to ignore noise, fine enough to
	// capture user-visible hitches.
	public const double SpikeThresholdMs = 100.0;
	public const int    SpikeRingSize    = 64;
	private static readonly List<SpikeRecord> _spikes = new();
	public static IReadOnlyList<SpikeRecord> Spikes => _spikes;

	private static long _sampleIndex;
	public  static long CurrentSampleIndex => _sampleIndex;
	internal static void AdvanceSampleIndex() => _sampleIndex++;

	internal static void RecordSpike(SpikeRecord r)
	{
		if (_spikes.Count >= SpikeRingSize) _spikes.RemoveAt(0);
		_spikes.Add(r);
	}
}
