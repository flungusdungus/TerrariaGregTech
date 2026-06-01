#nullable enable
using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace GregTechCEuTerraria.Config;

// World-wide gameplay flags. ServerSide so a dedicated server (or world host)
// dictates the rules and clients see them through tML's config sync.
public sealed class GTConfig : ModConfig
{
	public override ConfigScope Mode => ConfigScope.ServerSide;

	[DefaultValue(true)]
	public bool EnableBossDrops { get; set; } = true;

	// Multiplier applied to every upstream MC-tick-derived cadence - pipe
	// transfers, cover ticks, throughput windows, recipe ticks, anywhere
	// `TickScale.FromMcTicks` is used. 1.0 = fair real-time (1 simulated
	// second per 1 real-time second, matching upstream's 20 MC ticks/sec
	// intent). 2.5 = everything ticks 2.5x faster. Decimal allowed.
	[Range(0.1f, 10f)]
	[Increment(0.1f)]
	[DefaultValue(1.0f)]
	public float SimulationSpeed { get; set; } = 1.0f;

	// How often the server pushes per-entity state-sync packets (machine GUI
	// data, energy-net throughput, pipe-transfer counters) to nearby clients,
	// measured in Terraria ticks. Lower = smoother live progress bars + more
	// bandwidth. Higher = choppier UI + less traffic. Default 6 (= 10 Hz at
	// 60 fps) is the cable subsystem's original constant; tune higher on a
	// dedicated server with many viewers, lower for snappier solo MP.
	[Range(1, 60)]
	[Increment(1)]
	[DefaultValue(6)]
	public int NetworkSyncPeriod { get; set; } = 6;

	// Upstream ConfigHolder.machines.enableCleanroom parity (default true).
	// When false, every CleanroomCondition passes - effectively disables the
	// cleanroom requirement system globally.
	[DefaultValue(true)]
	public bool EnableCleanroom { get; set; } = true;

	// Upstream ConfigHolder.machines.cleanMultiblocks parity (default true).
	// When true, MultiblockControllerMachine instances bypass cleanroom checks
	// entirely - the rationale is multis are "self-cleaning" by construction.
	[DefaultValue(true)]
	public bool CleanMultiblocks { get; set; } = true;

	// Per-client debug overlay for custom bosses + their projectiles. When ON:
	//   * every projectile draws a colored hitbox outline (hostile red, friendly
	//     blue, neutral grey) with type-name label;
	//   * every live boss implementing IDebuggableBoss prints a screen-locked
	//     panel with its current attack name + timer + phase + HP% + background
	//     producer countdowns + live projectile counts + the last 5 attacks
	//     picked (rotating history).
	// Intended for tuning - flip on, fight the boss, screenshot/note "attack
	// XYZ at phase 1 felt too dense", flip off in normal play. Pure visual,
	// zero gameplay effect; safe to toggle mid-fight.
	[DefaultValue(false)]
	public bool DebugMobs { get; set; } = false;

	// Master switch for ALL profiler/diagnostic instrumentation: per-tick
	// sampling, the Profiler.Count/Gauge/Time hooks (energy-net / pipe-net /
	// machine-systemtick timers, net byte counters), the MP profiler-sync
	// packet, the memory probe + guard, and the in-game profiler window/button.
	// Enabled during development; flip off after the v1 release to drop the
	// overhead. When false every Profiler.* call is a cheap no-op.
	[DefaultValue(false)]
	public bool EnableProfiler { get; set; } = false;

	// Upstream ConfigHolder.machines.ldItemPipeMinDistance parity (default 50).
	// Minimum straight-line distance (in tiles) between a long-distance item
	// pipeline's input + output endpoints for the link to activate. 0 = no
	// minimum. Discourages using LD pipes for short hops where regular item
	// pipes are intended.
	[Range(0, 1000)]
	[Increment(10)]
	[DefaultValue(50)]
	public int LdItemPipeMinDistance { get; set; } = 50;

	// Upstream ConfigHolder.machines.ldFluidPipeMinDistance parity (default 50).
	// Minimum straight-line distance (in tiles) between a long-distance fluid
	// pipeline's input + output endpoints for the link to activate.
	[Range(0, 1000)]
	[Increment(10)]
	[DefaultValue(50)]
	public int LdFluidPipeMinDistance { get; set; } = 50;

	// Upstream ConfigHolder.machines.orderedAssemblyLineItems parity (default true).
	// When true, the Assembly Line requires the N-th recipe ITEM input to sit in the
	// N-th input bus (buses ordered left-to-right by world position). When false the
	// item inputs are matched/consumed unordered (any bus) via the standard
	// multiblock dispatch.
	[DefaultValue(true)]
	public bool OrderedAssemblyLineItems { get; set; } = true;

	// Upstream ConfigHolder.machines.orderedAssemblyLineFluids - upstream default is
	// TRUE. DEVIATION: defaulted to FALSE here. Fluid
	// ordering is widely disabled in modpacks and adds little; the toggle itself is
	// verbatim upstream, only the default value differs. When false the Assembly
	// Line matches/consumes fluid inputs unordered. Upstream's code gates fluid
	// ordering purely on this flag (its "requires item ordering" note is a config-UI
	// hint, not enforced) - we preserve that exact gate.
	[DefaultValue(false)]
	public bool OrderedAssemblyLineFluids { get; set; } = false;

	public override void OnChanged()
	{
		// Mirror the flag into the Profiler's static gate (read on every hot
		// Profiler.* call). tML invokes OnChanged after load + on any edit.
		TerrariaCompat.Profiler.Profiler.Enabled = EnableProfiler;
	}

	public static GTConfig Instance => ModContent.GetInstance<GTConfig>();
}
