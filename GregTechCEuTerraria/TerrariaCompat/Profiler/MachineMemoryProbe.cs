#nullable enable
using System.Collections.Generic;
using System.IO;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria.DataStructures;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Profiler;

// Memory attribution across mod-side state. Two signals, surfaced as profiler
// gauges (mem.machine_*, mem.subsystem.*, mem.total.*): retained-object COUNT
// per subsystem (the leak signal) and serialized save-state SIZE for the heavy
// state (machines, fluid-pipe tanks) via the same TagIO the MP sync uses.
//
// Leak reading guide:
//   - Count grows within a session, resets on world unload -> real mod leak.
//   - Count grows across Build+Reload, resets on full restart -> tML's reload
//     leak (old assemblies/statics), NOT ours.
//   - Static holders (recipes) are constant - context, shouldn't move.
//
// Slow cadence (ProfilerSystem ~5 s) - it serializes every machine + pipe cell.
public static class MachineMemoryProbe
{
	private static readonly Dictionary<string, int>  _counts = new();
	private static readonly Dictionary<string, long> _bytes  = new();

	public static void Sample()
	{
		SampleMachines();
		SampleSubsystems();
	}

	// === Per-machine count + serialized-state bytes, grouped by machine id ===
	private static void SampleMachines()
	{
		_counts.Clear();
		_bytes.Clear();

		long totalBytes = 0;
		int  totalCount = 0;
		int  coverCount = 0;

		foreach (var kv in TileEntity.ByID)
		{
			if (kv.Value is not MetaMachine m) continue;
			string id = m.Definition?.Id ?? m.GetType().Name;

			_counts.TryGetValue(id, out var c);
			_counts[id] = c + 1;
			totalCount++;

			long sz = SerializedBytes(m.SaveData);
			_bytes.TryGetValue(id, out var b);
			_bytes[id] = b + sz;
			totalBytes += sz;

			// Covers ride on the machine object graph (4 sides each).
			for (int s = 0; s < CoverSides.Count; s++)
				if (m.GetCoverAtSide((CoverSide)s) != null) coverCount++;
		}

		foreach (var kv in _counts)
			Profiler.Gauge("mem.machine_count", kv.Key, kv.Value);
		foreach (var kv in _bytes)
			Profiler.Gauge("mem.machine_state_kb", kv.Key, kv.Value >> 10);

		Profiler.Gauge("mem.machine_count", "TOTAL", totalCount);
		Profiler.Gauge("mem.machine_state_kb", "TOTAL", totalBytes >> 10);
		Profiler.Gauge("mem.subsystem", "machine_covers", coverCount);

		_machineStateBytes = totalBytes;
	}

	private static long _machineStateBytes;

	// === Wires / pipes / nets / recipes ====================================
	private static void SampleSubsystems()
	{
		// Cable (wire) layer - cells are tiny (material + size + flags); count is
		// the leak signal. Energy-net count is already under counts.energy_networks.
		Profiler.Gauge("mem.subsystem", "cable_cells",
			Pipelike.Cable.CableLayerSystem.Cables.Count);

		// Item pipes - cells + per-side cover/filter/robot-arm state + nets.
		Profiler.Gauge("mem.subsystem", "item_pipe_cells",  Pipelike.ItemPipe.ItemPipeLayerSystem.Pipes.Count);
		Profiler.Gauge("mem.subsystem", "item_pipe_sides",  Pipelike.ItemPipe.ItemPipeLayerSystem.AllSides.Count);
		Profiler.Gauge("mem.subsystem", "item_pipe_nets",   Pipelike.ItemPipe.ItemPipeNetSystem.Level.AllPipeNets.Count);

		// Fluid pipes - the per-cell tank state is the heavy part, so size it.
		long fluidStateBytes = 0;
		foreach (var kv in Pipelike.Fluid.FluidPipeLayerSystem.AllStates)
			fluidStateBytes += SerializedBytes(t => CopyInto(kv.Value.SaveTo(), t));
		Profiler.Gauge("mem.subsystem", "fluid_pipe_cells",   Pipelike.Fluid.FluidPipeLayerSystem.Pipes.Count);
		Profiler.Gauge("mem.subsystem", "fluid_pipe_sides",   Pipelike.Fluid.FluidPipeLayerSystem.AllSides.Count);
		Profiler.Gauge("mem.subsystem", "fluid_pipe_states",  Pipelike.Fluid.FluidPipeLayerSystem.AllStates.Count);
		Profiler.Gauge("mem.subsystem", "fluid_pipe_nets",    Pipelike.Fluid.FluidPipeNetSystem.Level.AllPipeNets.Count);
		Profiler.Gauge("mem.subsystem", "fluid_pipe_state_kb", fluidStateBytes >> 10);

		// Laser / optical pipes - tiny cells; count only.
		Profiler.Gauge("mem.subsystem", "laser_pipe_cells",   Pipelike.Laser.LaserPipeLayerSystem.Pipes.Count);
		Profiler.Gauge("mem.subsystem", "laser_pipe_nets",    Pipelike.Laser.LaserPipeNetSystem.Level.AllPipeNets.Count);
		Profiler.Gauge("mem.subsystem", "optical_pipe_cells", Pipelike.Optical.OpticalPipeLayerSystem.Pipes.Count);
		Profiler.Gauge("mem.subsystem", "optical_pipe_nets",  Pipelike.Optical.OpticalPipeNetSystem.Level.AllPipeNets.Count);

		// Static load-time holders (should be constant - context, not a leak).
		Profiler.Gauge("mem.subsystem", "recipes_total",     Recipes.RecipeRegistry.Count);
		Profiler.Gauge("mem.subsystem", "profiler_counters", Profiler.All.Count);

		// Per-world serializable-state roll-up (what we can actually measure):
		// machine state + fluid-pipe tanks. NOT total managed heap.
		Profiler.Gauge("mem.total", "world_state_kb", (_machineStateBytes + fluidStateBytes) >> 10);
	}

	// Serialized size in bytes of whatever `write` puts into a fresh tag.
	private static long SerializedBytes(System.Action<TagCompound> write)
	{
		try
		{
			var tag = new TagCompound();
			write(tag);
			using var ms = new MemoryStream();
			TagIO.ToStream(tag, ms);
			return ms.Length;
		}
		catch { return 0; }
	}

	private static void CopyInto(TagCompound src, TagCompound dst)
	{
		foreach (var kv in src) dst[kv.Key] = kv.Value;
	}
}
