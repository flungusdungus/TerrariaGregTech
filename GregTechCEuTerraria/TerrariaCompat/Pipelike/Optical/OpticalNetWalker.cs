#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Pipenet;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Optical;

// Port of com.gregtechceu.gtceu.common.pipelike.optical.OpticalNetWalker.
//
// Unlike the LASER walker (straight axis only - upstream's laser
// setConnection forbids any off-axis connection), the OPTICAL walker follows
// the pipe's ACTUAL connections (upstream uses the base PipeNetWalker, which
// walks every side gated by `isConnected`). Optical pipes allow bends but not
// splitting (<=2 connections per pipe, set reciprocally at placement). The walk
// therefore traverses the connection graph via IsValidPipe (both pipes opened
// the shared side) and stops at the first endpoint adjacent to a pipe in the
// path.
//
// === Documented adaptations =================================================
//
//   - `OpticalPipeBlockEntity` base class -> `OpticalPipeCell` struct (with the
//     Open bitmask that mirrors upstream's per-side connection state).
//   - `pipe.isConnected(side)` -> reciprocal Open-bit check in IsValidPipe.
//   - `GTCapability` neighbour lookup -> `WorldCapability.Get<T>(x, y)`.
public sealed class OpticalNetWalker : PipeNetWalker<OpticalPipeCell, OpticalPipeProperties, OpticalPipeNet>
{
	// Sentinel "walker tried, failed; don't cache, retry next call".
	public static readonly OpticalRoutePath FAILED_MARKER = new((0, 0), IODirection.None, 0);

	public OpticalRoutePath? RoutePath { get; private set; }

	private (int x, int y) _sourcePipe;
	private IODirection    _facingToHandler;

	private OpticalNetWalker(OpticalPipeNet net, (int x, int y) sourcePipe, int distance)
		: base(net, sourcePipe, distance) { }

	public static OpticalRoutePath? CreateNetData(OpticalPipeNet world, (int x, int y) sourcePipe, IODirection faceToSourceHandler)
	{
		try
		{
			var walker = new OpticalNetWalker(world, sourcePipe, 1);
			walker._sourcePipe      = sourcePipe;
			walker._facingToHandler = faceToSourceHandler;
			walker.TraversePipeNet();
			return walker.RoutePath;
		}
		catch
		{
			return FAILED_MARKER;
		}
	}

	protected override PipeNetWalker<OpticalPipeCell, OpticalPipeProperties, OpticalPipeNet> CreateSubWalker(
		OpticalPipeNet pipeNet, IODirection facingToNextPos, (int x, int y) nextPos, int walkedBlocks)
	{
		var walker = new OpticalNetWalker(pipeNet, nextPos, walkedBlocks);
		walker._sourcePipe      = _sourcePipe;
		walker._facingToHandler = _facingToHandler;
		return walker;
	}

	protected override bool TryGetCellAt((int x, int y) pos, out OpticalPipeCell cell)
	{
		var c = OpticalPipeLayerSystem.Pipes.CellAt(pos.x, pos.y);
		if (c is null) { cell = default; return false; }
		cell = c.Value;
		return true;
	}

	protected override void CheckPipe(OpticalPipeCell pipeTile, (int x, int y) pos) { }

	// Recurse only along an OPEN, reciprocal connection - this is what keeps the
	// walk on the non-branching path (and stops it crossing into an adjacent but
	// unconnected pipe).
	protected override bool IsValidPipe(OpticalPipeCell currentPipe, OpticalPipeCell otherPipe,
		(int x, int y) currentPos, IODirection side)
	{
		int bitHere = OpticalConn.Bit(side);
		int bitThere = OpticalConn.Bit(side.Opposite());
		return (currentPipe.Open & bitHere) != 0 && (otherPipe.Open & bitThere) != 0;
	}

	protected override void CheckNeighbour(
		OpticalPipeCell pipeNode, (int x, int y) pipePos, IODirection faceToNeighbour, object? neighbourTile)
	{
		// Skip the side we initially looked from on the source pipe.
		if (pipePos.x == _sourcePipe.x && pipePos.y == _sourcePipe.y && faceToNeighbour == _facingToHandler)
			return;

		var root = (OpticalNetWalker)Root;
		if (root.RoutePath != null) return;

		var (dx, dy) = faceToNeighbour.Offset();
		int hx = pipePos.x + dx;
		int hy = pipePos.y + dy;
		// Upstream stops at the first endpoint exposing EITHER capability.
		bool hasComputation = WorldCapability.Get<IOpticalComputationProvider>(hx, hy) != null;
		bool hasData        = WorldCapability.Get<IDataAccessHatch>(hx, hy) != null;
		if (hasComputation || hasData)
		{
			root.RoutePath = new OpticalRoutePath(pipePos, faceToNeighbour, WalkedBlocks);
			Stop();
		}
	}
}
