#nullable enable
using GregTechCEuTerraria.Api.Pipenet;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Optical;

// Optical pipes form non-branching paths: two pipes are connected only when
// BOTH have opened the shared side (reciprocal, <=2 per pipe - set at placement
// by OpticalConn.ConnectOnPlace, mirroring upstream OpticalPipeBlockEntity.
// setConnection's `getNumConnections() >= 2` reject). This drives ConnectionMask
// (renderer); the net topology is driven by the same open-mask via OnPipeAdded.
public sealed class OpticalPipeLayer : GridLayer<OpticalPipeCell>
{
	public override bool Connects(int x1, int y1, int x2, int y2)
	{
		var a = CellAt(x1, y1);
		var b = CellAt(x2, y2);
		if (a is null || b is null) return false;
		var side = SideBetween(x1, y1, x2, y2);
		if (side == IODirection.None) return false;
		int bitA = OpticalConn.Bit(side);
		int bitB = OpticalConn.Bit(side.Opposite());
		return (a.Value.Open & bitA) != 0 && (b.Value.Open & bitB) != 0;
	}

	private static IODirection SideBetween(int x1, int y1, int x2, int y2)
	{
		if (x2 == x1 && y2 == y1 - 1) return IODirection.Up;
		if (x2 == x1 && y2 == y1 + 1) return IODirection.Down;
		if (x2 == x1 - 1 && y2 == y1) return IODirection.Left;
		if (x2 == x1 + 1 && y2 == y1) return IODirection.Right;
		return IODirection.None;
	}
}
