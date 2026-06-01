#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.Api.Pipenet;

// LOCKED - API-faithful port of
// com.gregtechceu.gtceu.api.pipenet.PipeNetWalker. The public surface (abstract
// hooks, virtual hooks, entry methods, exposed getters) matches upstream so
// subclasses derived from this base mirror their upstream counterparts
// line-for-line.
//
// DO NOT modify the API; mirror upstream changes only.
//
// Documented structural adaptations (Terraria 2D, flat parallel layer):
//   - Upstream T extends `IPipeNode<?, ?>` (a BlockEntity-bound pipe node)
//     with per-side `isConnected(side)` / `isBlocked(side)` gating. Our cells
//     are struct values in a flat layer dict; connectivity is implicit (any
//     cell at a cardinal neighbor connects). The IsValidPipe virtual still
//     lets subclasses reject branches (e.g. material mismatch). The per-side
//     connection-state checks aren't reproducible without per-cell state we
//     don't carry - connectivity-gating subclasses would have to override
//     IsValidPipe with custom logic instead.
//   - BlockPos -> (int x, int y).
//   - Direction -> IODirection (4 sides, not 6).
//   - `BlockEntity neighbourTile` in CheckNeighbour -> `object?` (Terraria has
//     no unified entity type; subclasses cast to whatever they expect - for
//     energy, MetaMachine or null).
//   - `ServerLevel getLevel()` dropped - caller passes whatever world context
//     it needs through the constructor / sub-walker chain. We have no per-
//     world isolation since EnergyNetSystem is global.
//   - Recursive sub-walker pattern preserved verbatim - branching points
//     spawn sub-walkers via CreateSubWalker, joined via OnRemoveSubWalker.
//     Internal BFS state lives on the root walker.
//
// Type parameters:
//   - TCell     : per-cell data (e.g. CableCell). Equivalent to upstream T.
//   - TNodeData : per-cell metadata bag (e.g. WireProperties). Often equal
//                 to TCell when cells already carry their own properties.
//   - TNet      : the layer/dict store (e.g. CableLayer). Equivalent to
//                 upstream `Net extends PipeNet<NodeDataType>`.
public abstract class PipeNetWalker<TCell, TNodeData, TNet>
	where TCell : struct
	where TNet : class
{
	// === Public state (upstream-named getters; C# properties) ================

	public TNet PipeNet { get; }

	// Mutates as the walker advances. Upstream uses BlockPos.MutableBlockPos.
	public (int x, int y) CurrentPos { get; protected set; }

	// Steps walked from the source. Upstream's `getWalkedBlocks()`.
	public int WalkedBlocks { get; protected set; }

	// True after TraversePipeNet completes (or aborts). Upstream's `isInvalid()`.
	public bool Invalid { get; private set; }

	// True if the walker failed mid-walk (bad start pos, etc.). Upstream's
	// `isFailed()`.
	public bool Failed { get; private set; }

	// True while walking. Cleared by Stop() or natural completion. Upstream's
	// `isRunning()` always reads from the root walker.
	public bool Running => Root._running;
	private bool _running;

	// === Internal sub-walker plumbing ========================================

	protected PipeNetWalker<TCell, TNodeData, TNet> Root;
	private HashSet<(int x, int y)>? _walked;
	protected List<PipeNetWalker<TCell, TNodeData, TNet>>? Walkers;
	protected readonly List<IODirection> NextPipeFacings = new(5);
	protected readonly List<TCell> NextPipes = new(5);
	protected readonly List<(int x, int y)> NextPipePositions = new(5);
	protected TCell? CurrentPipe;
	private IODirection? _from;

	protected PipeNetWalker(TNet pipeNet, (int x, int y) sourcePipe, int walkedBlocks)
	{
		PipeNet = pipeNet;
		WalkedBlocks = walkedBlocks;
		CurrentPos = sourcePipe;
		Root = this;
	}

	// === Abstract hooks - subclass must implement ============================

	// Creates a sub walker. Will be called when a pipe has multiple valid
	// pipes. Upstream's `createSubWalker(pipeNet, facingToNextPos, nextPos,
	// walkedBlocks)`.
	protected abstract PipeNetWalker<TCell, TNodeData, TNet> CreateSubWalker(
		TNet pipeNet, IODirection facingToNextPos, (int x, int y) nextPos, int walkedBlocks);

	// Called every time the walker arrives at a new cell. Subclass uses this
	// to increment per-walker stats (e.g. accumulate per-cable loss).
	// Upstream's `checkPipe(pipeTile, pos)`.
	protected abstract void CheckPipe(TCell pipeTile, (int x, int y) pos);

	// Called once per pipe arrival, AFTER CheckPipe, BEFORE iterating cardinal
	// neighbors. New hook (no upstream equivalent - upstream's 3D voxel grid
	// forbids two things at the same cell). Use for **layered pipes** where a
	// foreground tile (machine / chest / endpoint) can coexist at the pipe's
	// own cell.
	//
	//   - Energy cables (background CableLayer) override this to look up an
	//     endpoint at the pipe's own world position - the only valid wire-
	//     to-machine connection per gameplay model "wire behind machine".
	//   - Item / fluid pipes (foreground) don't override - they can't have a
	//     foreground tile at the same cell as themselves.
	//
	// Default no-op so foreground pipes inherit cleanly.
	protected virtual void CheckSelfPos(TCell pipeTile, (int x, int y) pos) { }

	// Look up a cell at the given position. Returns false if no cell exists
	// (= not part of this pipe net). Adapts upstream's
	// `getBasePipeClass().isAssignableFrom(thisPipe.getClass())` check.
	protected abstract bool TryGetCellAt((int x, int y) pos, out TCell cell);

	// === Virtual hooks - subclass overrides selectively ======================

	// Checks the neighbour of the current pos. Use to emit routes / collect
	// stats from non-pipe neighbors. Upstream's `checkNeighbour`.
	//   - pipeNode        : the current pipe cell.
	//   - pipePos         : current pos.
	//   - faceToNeighbour : which side of pipePos we're looking at.
	//   - neighbourTile   : opaque neighbor object (MetaMachine / null
	//                       for energy walkers). Subclasses cast to whatever
	//                       they expect.
	protected virtual void CheckNeighbour(
		TCell pipeNode, (int x, int y) pipePos, IODirection faceToNeighbour, object? neighbourTile) { }

	// If the pipe is valid to perform a walk on. Upstream's `isValidPipe`.
	// Default true - subclasses gate by material/voltage/etc.
	protected virtual bool IsValidPipe(
		TCell currentPipe, TCell neighbourPipe, (int x, int y) pipePos, IODirection faceToNeighbour) => true;

	// The directions that this net can traverse from this pipe. Upstream's
	// `getSurroundingPipeSides()`. Default 4 cardinal sides.
	protected virtual IReadOnlyList<(IODirection side, int dx, int dy)> GetSurroundingPipeSides() =>
		MachineSides.Cardinal4;

	// Called when a sub walker is done walking. Subclasses use this to merge
	// sub-walker results back into the parent. Upstream's `onRemoveSubWalker`.
	protected virtual void OnRemoveSubWalker(PipeNetWalker<TCell, TNodeData, TNet> subWalker) { }

	// Resolve the neighbor "tile" at pos. Returned to CheckNeighbour as
	// `neighbourTile`. Subclasses control what counts (MachineCellResolver
	// lookup for energy, vanilla chest for items, ...). Default null = always
	// treat as empty.
	protected virtual object? ResolveNeighborTile((int x, int y) pos) => null;

	// === Public entry points =================================================

	public void TraversePipeNet() => TraversePipeNet(32768);

	// Starts walking the pipe net and gathers information.
	// maxWalks: cap to prevent stack overflow on pathological topologies.
	// Throws if the walker has already been used.
	public void TraversePipeNet(int maxWalks)
	{
		if (Invalid)
			throw new System.InvalidOperationException(
				"This walker already walked. Create a new one if you want to walk again");
		Root = this;
		_walked = new HashSet<(int, int)>();
		int i = 0;
		_running = true;
		while (_running && !Walk() && i++ < maxWalks) { }
		_running = false;
		Root._walked?.Clear();
		// upstream logs a warning if i >= maxWalks; we drop the logger
		// dependency.
		Invalid = true;
	}

	// Will cause the root walker to stop after the next walk.
	public void Stop() => Root._running = false;

	// === Internals ===========================================================

	private bool Walk()
	{
		if (Walkers == null)
		{
			if (!CheckPos())
			{
				Root.Failed = true;
				return true;
			}

			if (NextPipeFacings.Count == 0)
				return true;
			if (NextPipeFacings.Count == 1)
			{
				CurrentPos = NextPipePositions[0];
				CurrentPipe = NextPipes[0];
				_from = NextPipeFacings[0].Opposite();
				WalkedBlocks++;
				return !Running;
			}

			Walkers = new List<PipeNetWalker<TCell, TNodeData, TNet>>();
			for (int i = 0; i < NextPipeFacings.Count; i++)
			{
				var side = NextPipeFacings[i];
				var walker = CreateSubWalker(PipeNet, side, NextPipePositions[i], WalkedBlocks + 1)
					?? throw new System.InvalidOperationException("Walker can't be null");
				walker.Root = Root;
				walker.CurrentPipe = NextPipes[i];
				walker._from = side.Opposite();
				Walkers.Add(walker);
			}
		}

		for (int i = Walkers.Count - 1; i >= 0; i--)
		{
			var walker = Walkers[i];
			if (walker.Walk())
			{
				OnRemoveSubWalker(walker);
				Walkers.RemoveAt(i);
			}
		}

		return !Running || Walkers.Count == 0;
	}

	private bool CheckPos()
	{
		NextPipeFacings.Clear();
		NextPipes.Clear();
		NextPipePositions.Clear();
		if (CurrentPipe == null)
		{
			if (!TryGetCellAt(CurrentPos, out var cell))
				return false;
			CurrentPipe = cell;
		}
		var pipeTile = CurrentPipe.Value;
		CheckPipe(pipeTile, CurrentPos);
		CheckSelfPos(pipeTile, CurrentPos);
		Root._walked!.Add(CurrentPos);

		// check for surrounding pipes and item handlers
		foreach (var (accessSide, dx, dy) in GetSurroundingPipeSides())
		{
			// skip the side we came from. Upstream also skips sides reported
			// as blocked by the pipe network - we don't have per-cell
			// blocking, subclasses can gate via IsValidPipe instead.
			if (accessSide == _from) continue;

			var neighborPos = (CurrentPos.x + dx, CurrentPos.y + dy);
			object? neighborTile = ResolveNeighborTile(neighborPos);
			if (TryGetCellAt(neighborPos, out var otherPipe))
			{
				if (IsWalked(neighborPos)) continue;
				if (IsValidPipe(pipeTile, otherPipe, CurrentPos, accessSide))
				{
					NextPipeFacings.Add(accessSide);
					NextPipes.Add(otherPipe);
					NextPipePositions.Add(neighborPos);
					continue;
				}
			}
			CheckNeighbour(pipeTile, CurrentPos, accessSide, neighborTile);
		}
		return true;
	}

	protected bool IsWalked((int x, int y) pos) => Root._walked!.Contains(pos);
}
