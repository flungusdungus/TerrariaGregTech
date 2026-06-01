#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Profiler;
using Terraria;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Net;

// Server profiler counter snapshot. Lands on clients under "server." +
// category so the profiler UI shows both columns side-by-side. Best-effort:
// ships only the latest sample, not the full ring.
//
// Two-layer bandwidth defence (this packet used to be the single largest
// client-receive source AND its bursts perturbed the very FPS it measures -
// the deepest dips in the 2026-05-30 capture lined up with its 240 KB/s bursts):
//
//   1. DELTA-SYNC - only counters whose (ValueRaw, newestSample) changed since
//      the last broadcast ship a value. Static counters (per-machine mem gauges,
//      idle-machine timers) are skipped.
//   2. STRING INTERNING - a counter's Category/Name strings ship ONCE per epoch
//      as a "def" row keyed by its SyncId; every later broadcast ships only a
//      compact value row keyed by that id. A name like
//      machine_systemtick.by_type.WorkableElectric...Machine is ~50 bytes that
//      previously re-shipped at 10 Hz forever.
//
// Wire format (per broadcast):
//   byte   epoch
//   ushort defCount;  defCount x { ushort id, string category, string name, byte kind }
//   ushort valCount;  valCount x { ushort id, byte kind, <value by kind> }
//     where <value> is:
//       Gauge         -> long reading            (sample == ValueRaw, ship once)
//       Counter/Timer -> long raw + float32 rate (cumulative + per-sec sample;
//                       float32 is ample for a display graph at half the bytes)
//
// Value rows carry their own kind byte so they are SELF-DESCRIBING: the client
// can compute each row's width (and skip a row whose def it hasn't learned yet)
// without depending on def-before-value ordering. An earlier design omitted the
// kind byte and relied on the def always arriving first; in host-and-play the
// server ticks (and ships one-shot defs) before the client finishes loading, so
// the client missed early defs, then hit value rows for unknown ids it could not
// size -> the parser drifted and over-read the message (Read underflow). The kind
// byte + the periodic def resync below close that hole.
//
// Late-join / missed-def recovery: defs are one-shot per epoch, but every
// DefResyncPeriod broadcasts ALL defs are re-marked unsent so they re-drain over
// the next few broadcasts (capped). A client that missed a counter's first def
// (host-and-play world load, or a remote client joining mid-session) relearns it
// within one resync period. Values for a not-yet-learned id are skipped silently
// until then - no log spam, no desync.
//
// Self-instrumentation: the serialize cost (server) and deserialize cost
// (client) are themselves timed into profiler.* counters, so the profiler's own
// footprint is now first-class visible - the client deserialize folds into
// aggregate.frame_budget_ms_s, which previously hid this exact cost.
public static class ProfilerSyncPacket
{
	// Cap on def rows per broadcast so a flood of new counters (the first samples
	// after world load) or a resync drains over a few broadcasts instead of
	// bursting one giant packet. ~128 defs ~ 6 KB.
	private const int MaxDefsPerBroadcast = 128;

	// Re-send every counter's def this often so a client that missed the one-shot
	// originals recovers. ~10 s at 10 Hz; amortized cost is the full def table
	// (~34 KB) / 100 ~ 340 B/broadcast. _resyncCountdown starts low so the FIRST
	// resync fires a few seconds after load (closes the host-and-play join gap)
	// rather than waiting a full period.
	private const int DefResyncPeriod = 100;
	private static int _resyncCountdown = 30;

	// Reused across broadcasts so the scans allocate nothing.
	private static readonly List<ProfilerCounter> _defs = new(MaxDefsPerBroadcast);
	private static readonly List<ProfilerCounter> _vals = new(256);

	// Client-side: wire id -> local "server.*" mirror counter. Cleared when the
	// server's epoch byte changes (world reload reuses ids for new counters).
	private static readonly Dictionary<ushort, ProfilerCounter> _idMap = new(512);
	private static byte _clientEpoch;
	private static bool _clientEpochInit;

	public static void Broadcast()
	{
		if (!Profiler.Profiler.Enabled) return;
		if (Main.netMode != NetmodeID.Server) return;

		long t0 = Stopwatch.GetTimestamp();

		// Periodic def resync (late-join / missed-def recovery): re-mark all defs
		// unsent so they re-drain over the next few broadcasts.
		if (--_resyncCountdown <= 0)
		{
			_resyncCountdown = DefResyncPeriod;
			foreach (var c in Profiler.Profiler.All) c.SyncDefSent = false;
		}

		// Def block: up to MaxDefsPerBroadcast counters that haven't shipped their
		// string definition. Drains new + resync-pending defs across broadcasts.
		_defs.Clear();
		foreach (var c in Profiler.Profiler.All)
		{
			if (c.SyncDefSent) continue;
			_defs.Add(c);
			c.SyncDefSent = true;
			if (_defs.Count >= MaxDefsPerBroadcast) break;
		}

		// Value block: counters whose (raw, newest sample) changed since the last
		// ship. Independent of def status - value rows are self-describing (carry
		// their kind), so the client can parse + skip a row for an id whose def it
		// hasn't learned yet (it fills in at the next resync).
		_vals.Clear();
		foreach (var c in Profiler.Profiler.All)
		{
			int newest = Newest(c);
			double samp = c.Samples[newest];
			if (c.SyncInit && c.SyncLastVal == c.ValueRaw && c.SyncLastSamp == samp)
				continue;
			c.SyncLastVal = c.ValueRaw;
			c.SyncLastSamp = samp;
			c.SyncInit = true;
			_vals.Add(c);
		}

		if (_defs.Count == 0 && _vals.Count == 0)
		{
			RecordServerCost(t0, 0, 0);
			return;
		}

		var p = NetRouter.NewPacket(PacketType.ProfilerSync);
		p.Write(Profiler.Profiler.SyncEpoch);
		p.Write((ushort)_defs.Count);
		foreach (var c in _defs)
		{
			p.Write(c.SyncId);
			p.Write(c.Category);
			p.Write(c.Name);
			p.Write((byte)c.Kind);
		}
		p.Write((ushort)_vals.Count);
		foreach (var c in _vals)
		{
			p.Write(c.SyncId);
			p.Write((byte)c.Kind);
			if (c.Kind == ProfilerKind.Gauge)
			{
				// Gauge sample == ValueRaw (SampleAll), so the reading + the sample
				// are the same number - ship it once. Kept as long: gauge readings
				// (heap bytes, GC totals) exceed float32's exact range.
				p.Write(c.ValueRaw);
			}
			else
			{
				// Counter/Timer: cumulative raw (long, for dump fidelity) + the
				// per-sec rate as float32 (a display value - double is wasted).
				p.Write(c.ValueRaw);
				p.Write((float)c.Samples[Newest(c)]);
			}
		}
		p.Send();

		RecordServerCost(t0, _defs.Count, _vals.Count);
	}

	public static void HandleOnClient(BinaryReader reader)
	{
		if (Main.netMode != NetmodeID.MultiplayerClient) return;
		long t0 = Stopwatch.GetTimestamp();

		byte epoch = reader.ReadByte();
		if (!_clientEpochInit || epoch != _clientEpoch)
		{
			_idMap.Clear();
			_clientEpoch = epoch;
			_clientEpochInit = true;
		}

		int defN = reader.ReadUInt16();
		for (int i = 0; i < defN; i++)
		{
			ushort id   = reader.ReadUInt16();
			string cat  = reader.ReadString();
			string name = reader.ReadString();
			ProfilerKind kind = (ProfilerKind)reader.ReadByte();

			var c = Profiler.Profiler.GetOrCreate("server." + cat, name, kind);
			c.ExternallySampled = true;  // local SampleAll will skip this counter
			_idMap[id] = c;
		}

		int valN = reader.ReadUInt16();
		for (int i = 0; i < valN; i++)
		{
			ushort id = reader.ReadUInt16();
			ProfilerKind kind = (ProfilerKind)reader.ReadByte();

			// Read the full row by its self-describing kind, THEN apply if known.
			// A row whose def hasn't arrived yet (resync pending) is consumed and
			// dropped - never a parse desync.
			long raw; double samp;
			if (kind == ProfilerKind.Gauge)
			{
				raw  = reader.ReadInt64();
				samp = raw;
			}
			else
			{
				raw  = reader.ReadInt64();
				samp = reader.ReadSingle();
			}

			if (!_idMap.TryGetValue(id, out var c)) continue;  // def not learned yet
			c.ValueRaw = raw;
			c.LastSnapshotValue = raw;
			c.Samples[c.SampleHead] = samp;
			c.SampleHead = (c.SampleHead + 1) % c.Samples.Length;
		}

		// Self-instrumentation: the deserialize cost folds into the client's
		// aggregate.frame_budget_ms_s (the sum over Timer counters), so next time
		// the profiler's own per-frame cost is directly visible instead of hiding
		// as untracked main-thread work.
		Profiler.Profiler.AccumulateTimer("profiler", "sync_handle_client", Stopwatch.GetTimestamp() - t0);
		Profiler.Profiler.Gauge("profiler", "sync_rows_client", defN + valN);
	}

	private static int Newest(ProfilerCounter c) =>
		(c.SampleHead - 1 + c.Samples.Length) % c.Samples.Length;

	private static void RecordServerCost(long t0, int defCount, int valCount)
	{
		Profiler.Profiler.AccumulateTimer("profiler", "sync_serialize_server", Stopwatch.GetTimestamp() - t0);
		Profiler.Profiler.Gauge("profiler", "sync_defs_server", defCount);
		Profiler.Profiler.Gauge("profiler", "sync_vals_server", valCount);
	}
}
