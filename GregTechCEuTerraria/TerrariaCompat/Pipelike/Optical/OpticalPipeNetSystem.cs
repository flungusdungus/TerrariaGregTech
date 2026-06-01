#nullable enable
using GregTechCEuTerraria.Api.Pipenet;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Optical;

// Per-world LevelOpticalPipeNet owner. Mirrors LaserPipeNetSystem - rebuilds
// the level pipenet from cells when the OpticalPipeLayer is dirty.
//
// DEVIATION (consistency-driven): upstream's LevelPipeNet persists
// nets via Forge SavedData; we mirror the cable/item/fluid/laser pipe "derived
// from cells, no separate save" model. The PipeNet hierarchy stays verbatim.
public sealed class OpticalPipeNetSystem : ModSystem
{
	private static LevelOpticalPipeNet? _level;

	public static LevelOpticalPipeNet Level => _level ??= new LevelOpticalPipeNet();

	public override void ClearWorld() => _level = new LevelOpticalPipeNet();

	// PostUpdateEverything runs on MP clients too (PostUpdateWorld doesn't).
	public override void PostUpdateEverything() => MaybeRebuild();

	public override void PostUpdateWorld()
	{
		using (Profiler.Profiler.Time("tick", "optical_pipe_net"))
		{
			MaybeRebuild();
			Profiler.Profiler.Gauge("counts", "optical_pipes", OpticalPipeLayerSystem.Pipes.Count);
			Profiler.Profiler.Gauge("counts", "optical_pipe_nets", Level.AllPipeNets.Count);
		}
	}

	public static void MaybeRebuild()
	{
		if (!OpticalPipeLayerSystem.Pipes.IsDirty) return;

		_level = new LevelOpticalPipeNet();
		foreach (var kv in OpticalPipeLayerSystem.Pipes.All)
			OnPipeAdded(kv.Key.x, kv.Key.y);
		OpticalPipeLayerSystem.Pipes.ClearDirty();
	}

	public static void OnPipeAdded(int x, int y)
	{
		if (Level.GetNetFromPos((x, y)) is not null) return;
		// Open mask = the <=2 reciprocal connections computed at placement. The
		// net's CanNodesConnect only merges two pipes that both opened the
		// shared side -> non-branching paths (vs ALL_OPENED merging everything).
		var cell = OpticalPipeLayerSystem.Pipes.CellAt(x, y);
		int open = cell?.Open ?? Node<OpticalPipeProperties>.ALL_OPENED;
		Level.AddNode((x, y), OpticalPipeProperties.INSTANCE,
			mark: Node<OpticalPipeProperties>.DEFAULT_MARK,
			openConnections: open,
			isActive: false);
	}

	public static void OnPipeRemoved(int x, int y) => Level.RemoveNode((x, y));
}
