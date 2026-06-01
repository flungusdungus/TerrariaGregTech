#nullable enable
namespace GregTechCEuTerraria.Api;

// Tick-rate scaling between upstream Minecraft (20 ticks/sec) and Terraria
// (60 ticks/sec), with a configurable per-world simulation-speed multiplier.
//
// Upstream code that uses raw tick counts like `5`, `20`, `60` etc. encodes
// a real-time cadence in MC ticks - verbatim-porting those constants into
// Terraria makes the logic run 3x too fast (cover transfer cycles,
// throughput-window resets, anywhere the upstream constant means "N
// seconds"). `FromMcTicks` is the single place that translates an upstream
// MC-tick count to Terraria ticks, with the player's `GTConfig.SimulationSpeed`
// multiplier applied so a value of 2.5 makes everything tick 2.5x faster
// than fair real-time.
//
// Math: terraria_ticks = mc_ticks x (60 / 20) / SimulationSpeed
//                       = mc_ticks x 3 / SimulationSpeed
//
//   SimulationSpeed = 1.0  -> 1 sec sim = 1 sec real (default; upstream-faithful real-time)
//   SimulationSpeed = 2.5  -> 1 sec sim = 0.4 sec real (2.5x faster)
//   SimulationSpeed = 3.0  -> 1 sec sim = 1 MC tick (i.e. upstream's MC clock; events fire as fast as if running in vanilla MC)
//
// Use `FromMcTicks` at every site that translates upstream MC ticks
// into a Terraria-tick cadence. Mark every site with a comment quoting the
// upstream MC-tick value for grep-against-upstream parity.
public static class TickScale
{
	public const int McTickRate       = 20;   // Minecraft ticks per second
	public const int TerrariaTickRate = 60;   // Terraria ticks per second

	// Scale an upstream MC-tick count to Terraria ticks at the current
	// simulation speed. Clamped to a minimum of 1 so a high SimulationSpeed
	// never produces a 0-tick cadence (which would be a divide-by-zero in
	// `timer % 0` modulo gates).
	public static int FromMcTicks(int mcTicks)
	{
		float sim = SimulationSpeed;
		if (sim <= 0f) sim = 1f;
		float raw = mcTicks * (TerrariaTickRate / (float)McTickRate) / sim;
		int scaled = (int)System.Math.Round(raw);
		return scaled < 1 ? 1 : scaled;
	}

	// Decoupled from the GTConfig type so the Api namespace doesn't depend
	// on Config (which depends on tML). The Mod's Load hook seeds this with
	// a delegate that reads from GTConfig at runtime; defaults to 1.0 in
	// SP / before mod load so calls before Load() still work.
	public static System.Func<float>? SimulationSpeedProvider;
	public static float SimulationSpeed => SimulationSpeedProvider?.Invoke() ?? 1.0f;
}
