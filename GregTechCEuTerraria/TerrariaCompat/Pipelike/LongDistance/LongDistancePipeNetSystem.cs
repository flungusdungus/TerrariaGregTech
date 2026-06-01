#nullable enable
using GregTechCEuTerraria.Api.Pipenet;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.LongDistance;

// Per-world LevelLongDistancePipeNet owner. Mirrors LaserPipeNetSystem - rebuilds
// the level pipenet from cells when the layer is dirty. No per-tick maintenance:
// LD nets carry no per-cell counters, and the wormhole transfer is driven by the
// endpoint machines' capability exposure (a pipe pushing into the input endpoint),
// not by a net tick.
//
// DEVIATION (consistency-driven): upstream's LongDistanceNetwork.WorldData
// is a Forge SavedData with its own NBT + a background NetworkBuilder thread that
// force-loads chunks. We mirror the cable/item/fluid/laser "derived from cells, no
// separate save, synchronous rebuild" model - Terraria keeps the whole world in
// Main.tile so there are no chunks to load and no thread is needed.
public sealed class LongDistancePipeNetSystem : ModSystem
{
	private static LevelLongDistancePipeNet? _level;

	public static LevelLongDistancePipeNet Level => _level ??= new LevelLongDistancePipeNet();

	public override void ClearWorld() => _level = new LevelLongDistancePipeNet();

	// PostUpdateEverything runs on MP clients too (PostUpdateWorld doesn't) -
	// matches LaserPipeNetSystem.
	public override void PostUpdateEverything() => MaybeRebuild();

	public override void PostUpdateWorld()
	{
		using (Profiler.Profiler.Time("tick", "long_distance_pipe_net"))
		{
			MaybeRebuild();
			Profiler.Profiler.Gauge("counts", "ld_pipes", LongDistancePipeLayerSystem.Pipes.Count);
			Profiler.Profiler.Gauge("counts", "ld_pipe_nets", Level.AllPipeNets.Count);
		}
	}

	public static void MaybeRebuild()
	{
		if (!LongDistancePipeLayerSystem.Pipes.IsDirty) return;

		_level = new LevelLongDistancePipeNet();
		foreach (var kv in LongDistancePipeLayerSystem.Pipes.All)
			OnPipeAdded(kv.Key.x, kv.Key.y);
		LongDistancePipeLayerSystem.Pipes.ClearDirty();

		// Any topology change can re-pair endpoints (a net split/merge changes
		// which input + output share a component). Force a fresh link resolve.
		LongDistanceEndpointRegistry.InvalidateAll();
	}

	public static void OnPipeAdded(int x, int y)
	{
		// Idempotent - lets world-load callers replay safely.
		if (Level.GetNetFromPos((x, y)) is not null) return;

		var cell = LongDistancePipeLayerSystem.Pipes.CellAt(x, y);
		// Item=1, Fluid=2 - keeps the two types in separate connected components.
		int mark = (cell?.Type ?? LongDistancePipeType.Item).NodeMark();
		Level.AddNode((x, y), LongDistancePipeProperties.INSTANCE,
			mark: mark,
			openConnections: Node<LongDistancePipeProperties>.ALL_OPENED,
			isActive: false);
	}

	public static void OnPipeRemoved(int x, int y)
	{
		Level.RemoveNode((x, y));
		LongDistanceEndpointRegistry.InvalidateAll();
	}
}
