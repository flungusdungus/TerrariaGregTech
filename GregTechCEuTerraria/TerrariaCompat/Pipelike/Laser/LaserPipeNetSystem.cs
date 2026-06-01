#nullable enable
using GregTechCEuTerraria.Api.Pipenet;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Laser;

// Per-world LevelLaserPipeNet owner. Mirrors ItemPipeNetSystem - rebuilds
// the level pipenet from cells when the LaserPipeLayer is dirty, and runs
// any per-tick maintenance (none today - laser pipes have no per-cell
// counters / per-side covers).
//
// DEVIATION (consistency-driven): upstream's LevelPipeNet
// persists nets via Forge SavedData; we mirror the cable+item+fluid pipe
// "derived from cells, no separate save" model. The PipeNet hierarchy
// itself stays verbatim.
public sealed class LaserPipeNetSystem : ModSystem
{
	private static LevelLaserPipeNet? _level;

	public static LevelLaserPipeNet Level => _level ??= new LevelLaserPipeNet();

	public override void ClearWorld() => _level = new LevelLaserPipeNet();

	// PostUpdateEverything runs on MP clients too (PostUpdateWorld doesn't) -
	// matches ItemPipeNetSystem.
	public override void PostUpdateEverything() => MaybeRebuild();

	public override void PostUpdateWorld()
	{
		using (Profiler.Profiler.Time("tick", "laser_pipe_net"))
		{
			MaybeRebuild();
			Profiler.Profiler.Gauge("counts", "laser_pipes", LaserPipeLayerSystem.Pipes.Count);
			Profiler.Profiler.Gauge("counts", "laser_pipe_nets", Level.AllPipeNets.Count);
		}
	}

	public static void MaybeRebuild()
	{
		if (!LaserPipeLayerSystem.Pipes.IsDirty) return;

		_level = new LevelLaserPipeNet();
		foreach (var kv in LaserPipeLayerSystem.Pipes.All)
			OnPipeAdded(kv.Key.x, kv.Key.y);
		LaserPipeLayerSystem.Pipes.ClearDirty();
	}

	public static void OnPipeAdded(int x, int y)
	{
		// Idempotent - lets world-load callers replay safely.
		if (Level.GetNetFromPos((x, y)) is not null) return;

		// Open mask = the straight-only reciprocal connections from placement.
		// CanNodesConnect only merges pipes that both opened the shared side, so
		// crossing laser lines stay separate nets (no blob / cross).
		var cell = LaserPipeLayerSystem.Pipes.CellAt(x, y);
		int open = cell?.Open ?? Node<LaserPipeProperties>.ALL_OPENED;
		Level.AddNode((x, y), LaserPipeProperties.INSTANCE,
			mark: Node<LaserPipeProperties>.DEFAULT_MARK,
			openConnections: open,
			isActive: false);
	}

	public static void OnPipeRemoved(int x, int y) => Level.RemoveNode((x, y));
}
