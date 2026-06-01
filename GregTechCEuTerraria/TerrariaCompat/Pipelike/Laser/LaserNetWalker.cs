#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Pipenet;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Laser;

// Verbatim port of com.gregtechceu.gtceu.common.pipelike.laser.LaserNetWalker.
//
// Walks a laser pipe net STRICTLY along ONE axis (= matches upstream's
// `getSurroundingPipeSides` returning only the two facings in the source
// axis). Stops at the FIRST ILaserContainer it finds adjacent to a pipe in
// the line; the resulting `LaserRoutePath` is the unique point-to-point
// destination from that source side.
//
// === Documented adaptations =================================================
//
//   - `Direction.Axis` (X/Y/Z) -> 2D enum-equivalent: horizontal axis
//     (Left/Right) vs vertical axis (Up/Down).
//   - `LaserPipeBlockEntity` base class -> `LaserPipeCell` struct payload.
//   - `LaserPipeNet` -> `LaserPipeNet` (our port).
//   - `GTCapability.CAPABILITY_LASER` neighbour lookup -> `WorldCapability.
//     Get<ILaserContainer>(x, y)` (our 2D capability resolver). Side gating
//     is on the hatch's `SideInputCondition`/`SideOutputCondition` lambda,
//     so the walker doesn't pass a side here - the hatch self-gates when
//     energy is pushed.
public sealed class LaserNetWalker : PipeNetWalker<LaserPipeCell, LaserPipeProperties, LaserPipeNet>
{
	// Sentinel that means "walker tried, failed catastrophically; don't cache,
	// retry next call". Mirrors upstream's `FAILED_MARKER`.
	public static readonly LaserRoutePath FAILED_MARKER = new((0, 0), IODirection.None, 0);

	// Set on the ROOT walker when the first endpoint is found. Sub-walkers
	// read/write `Root.RoutePath` (same shape as upstream).
	public LaserRoutePath? RoutePath { get; private set; }

	// The source pipe + the face we initially looked at - these constrain the
	// axis-aligned traversal and prevent the walker from immediately re-visiting
	// the handler we started from. Verbatim with upstream.
	private (int x, int y) _sourcePipe;
	private IODirection    _facingToHandler;
	private Axis           _axis;

	// 2D axis enum - replaces upstream's `Direction.Axis` (which has X/Y/Z).
	private enum Axis { Horizontal, Vertical }

	private LaserNetWalker(LaserPipeNet net, (int x, int y) sourcePipe, int distance)
		: base(net, sourcePipe, distance) { }

	// Entry point - constructs the root walker, primes its axis from the source
	// facing, runs the walk, returns the RoutePath (or null if no endpoint, or
	// FAILED_MARKER if the walk threw). Mirrors upstream `createNetData`.
	public static LaserRoutePath? CreateNetData(LaserPipeNet world, (int x, int y) sourcePipe, IODirection faceToSourceHandler)
	{
		try
		{
			var walker = new LaserNetWalker(world, sourcePipe, 1);
			walker._sourcePipe      = sourcePipe;
			walker._facingToHandler = faceToSourceHandler;
			walker._axis            = AxisOf(faceToSourceHandler);
			walker.TraversePipeNet();
			return walker.RoutePath;
		}
		catch
		{
			return FAILED_MARKER;
		}
	}

	private static Axis AxisOf(IODirection dir) => dir switch
	{
		IODirection.Left or IODirection.Right => Axis.Horizontal,
		IODirection.Up   or IODirection.Down  => Axis.Vertical,
		_                                     => Axis.Horizontal,
	};

	// Single-axis side iteration - mirrors upstream's X/Y/Z arrays. We expose
	// only the two facings on the source axis so a perpendicular branch never
	// gets walked into.
	private static readonly IReadOnlyList<(IODirection side, int dx, int dy)> HorizontalSides =
		new (IODirection, int, int)[] { (IODirection.Left, -1, 0), (IODirection.Right, 1, 0) };
	private static readonly IReadOnlyList<(IODirection side, int dx, int dy)> VerticalSides =
		new (IODirection, int, int)[] { (IODirection.Up, 0, -1), (IODirection.Down, 0, 1) };

	protected override IReadOnlyList<(IODirection side, int dx, int dy)> GetSurroundingPipeSides() =>
		_axis switch { Axis.Horizontal => HorizontalSides, _ => VerticalSides };

	protected override PipeNetWalker<LaserPipeCell, LaserPipeProperties, LaserPipeNet> CreateSubWalker(
		LaserPipeNet pipeNet, IODirection facingToNextPos, (int x, int y) nextPos, int walkedBlocks)
	{
		var walker = new LaserNetWalker(pipeNet, nextPos, walkedBlocks);
		walker._sourcePipe      = _sourcePipe;
		walker._facingToHandler = _facingToHandler;
		walker._axis            = _axis;
		return walker;
	}

	protected override bool TryGetCellAt((int x, int y) pos, out LaserPipeCell cell)
	{
		var c = LaserPipeLayerSystem.Pipes.CellAt(pos.x, pos.y);
		if (c is null) { cell = default; return false; }
		cell = c.Value;
		return true;
	}

	// No per-pipe stats to collect - mirrors upstream's empty `checkPipe`.
	protected override void CheckPipe(LaserPipeCell pipeTile, (int x, int y) pos) { }

	// At each pipe, look at each cardinal side ON THE WALK AXIS. If the
	// adjacent cell carries an ILaserContainer, we found the endpoint -
	// record the route and stop the walk. Mirrors upstream `checkNeighbour`.
	protected override void CheckNeighbour(
		LaserPipeCell pipeNode, (int x, int y) pipePos, IODirection faceToNeighbour, object? neighbourTile)
	{
		// Same guard upstream uses: skip the side we initially looked from on
		// the source pipe (= the side that holds the SOURCE handler, not the
		// destination). Without this, a one-pipe straight line into a hatch
		// would land back on the source endpoint instead of walking through.
		if (pipePos.x == _sourcePipe.x && pipePos.y == _sourcePipe.y && faceToNeighbour == _facingToHandler)
			return;

		var root = (LaserNetWalker)Root;
		if (root.RoutePath != null) return;

		var (dx, dy) = faceToNeighbour.Offset();
		int hx = pipePos.x + dx;
		int hy = pipePos.y + dy;
		var handler = WorldCapability.Get<ILaserContainer>(hx, hy);
		if (handler != null)
		{
			root.RoutePath = new LaserRoutePath(pipePos, faceToNeighbour, WalkedBlocks);
			Stop();
		}
	}
}
