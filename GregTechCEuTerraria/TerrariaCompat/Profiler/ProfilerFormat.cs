#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Profiler;
using Microsoft.Xna.Framework;

namespace GregTechCEuTerraria.TerrariaCompat.Profiler;

// Display helpers - keeps the formatting + heat-map logic in one place so
// the UI can stay focused on layout. Three concerns:
//
//   1. Format(counter, value) -> human-readable string with the right unit.
//      Bytes get B/KB/MB; timers get "X ms (Y%)" where Y is % of frame budget;
//      counts get thousands separators; gauges named *_mb get "MB" appended.
//
//   2. Severity(counter, value) -> enum from Calm -> Hot, used to drive a
//      left-edge color stripe per row. Different thresholds per counter
//      family (a 10 ms/s Timer is yellow; 10 KB/s bytes counter is green).
//
//   3. Category grouping helpers - Title(category) for the section header.
public enum ProfilerSeverity : byte { Neutral, Calm, Warm, Hot }

public static class ProfilerFormat
{
	// One vanilla Terraria frame ~ 1000/60 ms. Used by the graph reference line.
	public const double FrameMs = 1000.0 / 60.0;

	// Timer values are stored as ms-of-CPU-work per SECOND of wall time, so
	// the % of wall clock is `ms / 10` (= ms / 1000 * 100). Don't divide by
	// FrameMs - that was a previous version's bug that read 200 ms/s as
	// "1200% of frame" instead of "20% wall clock".
	private static double WallClockPercent(double msPerSec) => msPerSec / 10.0;

	// -- Formatting -------------------------------------------------------

	public static string Format(ProfilerCounter c, double value)
	{
		// Bytes counters (name pattern: net.*.bytes.*) - show B / KB / MB per sec.
		if (c.Kind == ProfilerKind.Counter && c.Category.EndsWith(".bytes", System.StringComparison.Ordinal))
			return FormatBytesPerSec(value);

		// Timer counters -> "X.X ms (Y.Y% wall)" where percent is of wall clock.
		if (c.Kind == ProfilerKind.Timer)
		{
			double pct = WallClockPercent(value);
			return $"{FormatMs(value)} ({pct:0.0}%)";
		}

		// Per-second counters - thousands separator, /s suffix.
		if (c.Kind == ProfilerKind.Counter)
		{
			if (value >= 10000) return $"{value:N0}/s";
			if (value >= 100)   return $"{value:N0}/s";
			if (value >= 10)    return $"{value:0.0}/s";
			return $"{value:0.00}/s";
		}

		// Gauges - smart by name.
		if (c.Name.EndsWith("_mb", System.StringComparison.Ordinal))
			return $"{value:N0} MB";
		if (c.Name == "fps")
			return $"{value:0.0}";
		if (value >= 10000 || value <= -10000) return $"{value:N0}";
		if (value >= 100)                       return $"{value:N0}";
		if (value >= 10)                        return $"{value:0.0}";
		return $"{value:0.##}";
	}

	private static string FormatBytesPerSec(double bps)
	{
		if (bps >= 1_000_000) return $"{bps / 1_000_000:0.00} MB/s";
		if (bps >= 1_000)     return $"{bps / 1_000:0.0} KB/s";
		return $"{bps:0} B/s";
	}

	private static string FormatMs(double ms)
	{
		if (ms >= 100) return $"{ms:0} ms";
		if (ms >= 10)  return $"{ms:0.0} ms";
		return $"{ms:0.00} ms";
	}

	// -- Severity ---------------------------------------------------------
	// Thresholds are intentionally conservative - Hot means "this is worth a
	// look", not "the world is on fire". Calm = green / Warm = yellow / Hot =
	// red; Neutral = no opinion (counts without a clear good-vs-bad axis,
	// gauges where higher is just informational).

	public static ProfilerSeverity Severity(ProfilerCounter c, double value)
	{
		// Timers - % of wall clock per second. Calibrated against measured
		// host-and-play data: server.tick.fluid_pipe_net ~ 0.2% (clearly fine),
		// server.tick.machine_systemtick ~ 18% (visible hitch source).
		//   < 1%   neutral (background noise)
		//   1-5%   calm (visible but cheap)
		//   5-15%  warm (worth a look if it's sustained)
		//   >=15%  hot (eating real frame budget)
		if (c.Kind == ProfilerKind.Timer)
		{
			double pct = WallClockPercent(value);
			if (pct >= 15) return ProfilerSeverity.Hot;
			if (pct >= 5)  return ProfilerSeverity.Warm;
			if (pct >= 1)  return ProfilerSeverity.Calm;
			return ProfilerSeverity.Neutral;
		}

		// Network bytes per second. Post-dirty-skip MachineStateSync runs at
		// ~10 KB/s on a 394-machine base; the pre-fix peak was 975 KB/s. We
		// want ~10 KB/s to read calm and any sustained 6-figure rate to read
		// at least warm.
		//   < 1 KB/s  neutral
		//   1-50      calm
		//   50-200    warm
		//   >=200     hot
		if (c.Kind == ProfilerKind.Counter && c.Category.EndsWith(".bytes", System.StringComparison.Ordinal))
		{
			if (value >= 200_000) return ProfilerSeverity.Hot;
			if (value >=  50_000) return ProfilerSeverity.Warm;
			if (value >=   1_000) return ProfilerSeverity.Calm;
			return ProfilerSeverity.Neutral;
		}

		// Network packet rates. Healthy ambient broadcasts cluster at 10-50/s
		// (one per pipe-net per sync tick); the pre-fix MachineStateSync
		// flood was 2,810/s. So:
		//   < 50    neutral
		//   50-500  calm
		//   500-2k  warm
		//   >=2k    hot
		if (c.Kind == ProfilerKind.Counter
			&& (c.Category == "net.in.count"  || c.Category == "net.out.count"
			 || c.Category == "server.net.in.count" || c.Category == "server.net.out.count"))
		{
			if (value >= 2_000) return ProfilerSeverity.Hot;
			if (value >= 500)   return ProfilerSeverity.Warm;
			if (value >= 50)    return ProfilerSeverity.Calm;
			return ProfilerSeverity.Neutral;
		}

		// Aggregate gauges - heat-mapped using the same wall-clock /
		// bandwidth scales as their member counters.
		if (c.Kind == ProfilerKind.Gauge && (c.Category == "aggregate" || c.Category == "server.aggregate"))
		{
			if (c.Name == "frame_budget_ms_s")
			{
				double pct = WallClockPercent(value);
				if (pct >= 50) return ProfilerSeverity.Hot;   // 1/2 of the wall clock spent in our code
				if (pct >= 25) return ProfilerSeverity.Warm;
				if (pct >= 5)  return ProfilerSeverity.Calm;
				return ProfilerSeverity.Neutral;
			}
			if (c.Name == "net_in_bytes_s")
			{
				if (value >= 500_000) return ProfilerSeverity.Hot;
				if (value >= 100_000) return ProfilerSeverity.Warm;
				if (value >=   5_000) return ProfilerSeverity.Calm;
				return ProfilerSeverity.Neutral;
			}
		}

		// Engine gauges. fps low = bad; heap above the LOH-typical mod budget
		// = worth flagging. Don't color counts (machines / pipes / nets).
		if (c.Kind == ProfilerKind.Gauge && (c.Category == "engine" || c.Category == "server.engine"))
		{
			if (c.Name == "fps")
			{
				if (value > 0 && value < 30) return ProfilerSeverity.Hot;
				if (value > 0 && value < 50) return ProfilerSeverity.Warm;
				if (value >= 55) return ProfilerSeverity.Calm;
				return ProfilerSeverity.Neutral;  // 0 = not yet sampled
			}
			if (c.Name == "managed_heap_mb")
			{
				// Client-side baseline in this project is ~6-8 GB just from
				// loading the mod (recipe tries + baked textures). Don't
				// scream until it's well past that.
				if (value >= 12_000) return ProfilerSeverity.Hot;
				if (value >=  8_000) return ProfilerSeverity.Warm;
				if (value >=  1_000) return ProfilerSeverity.Calm;
				return ProfilerSeverity.Neutral;
			}
			// GC totals are monotonic gauges; the interesting axis is the
			// per-window DELTA (which the graph shows). Don't color the
			// cumulative value.
		}

		// `net.skipped.*` is good-when-high; don't flag.
		return ProfilerSeverity.Neutral;
	}

	// -- Colors -----------------------------------------------------------

	public static Color StripeColor(ProfilerSeverity s) => s switch
	{
		ProfilerSeverity.Hot     => new Color(225,  80,  60),
		ProfilerSeverity.Warm    => new Color(235, 195,  70),
		ProfilerSeverity.Calm    => new Color( 95, 180, 110),
		_                        => new Color( 80,  85, 100),
	};

	public static Color ValueColor(ProfilerCounter c, ProfilerSeverity s)
	{
		if (s == ProfilerSeverity.Hot)   return new Color(255, 150, 130);
		if (s == ProfilerSeverity.Warm)  return new Color(255, 220, 130);
		if (s == ProfilerSeverity.Calm)  return new Color(170, 235, 180);
		// Neutral - gauges read bluish, counters/timers read warm-white.
		return c.Kind == ProfilerKind.Gauge ? new Color(200, 230, 255) : new Color(230, 230, 230);
	}

	// -- Category titles --------------------------------------------------
	// Pretty names for the section headers. Unknown categories fall through
	// to their raw key.
	public static string CategoryTitle(string category) => category switch
	{
		"engine"            => "Engine (client)",
		"server.engine"     => "Engine (server)",
		"counts"            => "World counts (client view)",
		"server.counts"     => "World counts (server)",
		"tick"              => "Tick cost (client)",
		"server.tick"       => "Tick cost (server)",
		"net.in.count"      => "Packets in - count",
		"net.in.bytes"      => "Packets in - bytes",
		"net.out.count"     => "Packets out - count",
		"net.skipped"       => "Broadcasts skipped (dirty-skip)",
		"server.net.in.count"  => "Packets in - count (server)",
		"server.net.in.bytes"  => "Packets in - bytes (server)",
		"server.net.out.count" => "Packets out - count (server)",
		"server.net.skipped"   => "Broadcasts skipped (server)",
		"aggregate"         => "Aggregates",
		_                   => category,
	};

	// Ordering hint - smaller = drawn earlier. Categories not listed sort by
	// their raw key after the listed ones. Keeps the most-actionable info
	// (aggregates + tick cost + network) at the top.
	public static int CategoryOrder(string category) => category switch
	{
		"aggregate"            => 0,
		"server.tick"          => 10,
		"tick"                 => 11,
		"net.in.bytes"         => 20,
		"server.net.in.bytes"  => 21,
		"net.in.count"         => 22,
		"server.net.in.count"  => 23,
		"net.out.count"        => 24,
		"server.net.out.count" => 25,
		"net.skipped"          => 26,
		"server.net.skipped"   => 27,
		"counts"               => 30,
		"server.counts"        => 31,
		"engine"               => 40,
		"server.engine"        => 41,
		_                      => 1000,
	};
}
