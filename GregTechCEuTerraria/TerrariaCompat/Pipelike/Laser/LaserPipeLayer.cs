#nullable enable
using GregTechCEuTerraria.Api.Pipenet;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Laser;

// Laser pipes connect STRAIGHT only: two pipes are linked iff BOTH opened the
// shared side (reciprocal open-mask set at placement by LaserConn.ConnectOnPlace,
// which forbids any off-axis connection). Drives both the net topology (via
// OnPipeAdded's open mask) and ConnectionMask (renderer) - so a laser cross /
// turn no longer merges or renders as a "+".
public sealed class LaserPipeLayer : GridLayer<LaserPipeCell>
{
	public override bool Connects(int x1, int y1, int x2, int y2)
	{
		var a = CellAt(x1, y1);
		var b = CellAt(x2, y2);
		if (a is null || b is null) return false;
		var side = SideBetween(x1, y1, x2, y2);
		if (side == IODirection.None) return false;
		int bitA = LaserConn.Bit(side);
		int bitB = LaserConn.Bit(side.Opposite());
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
