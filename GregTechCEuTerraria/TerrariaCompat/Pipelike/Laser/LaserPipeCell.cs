#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Laser;

// One placed laser pipe. Carries the per-side open-connection bitmask so laser
// pipes can only connect STRAIGHT (no turns, no crosses) - mirror of upstream
// LaserPipeBlockEntity.setConnection, which rejects any connection off the
// requested side's axis. The mask uses CoverSide ordinals (Up=0,Down=1,Left=2,
// Right=3) so it feeds straight into Node.OpenConnections.
public readonly record struct LaserPipeCell
{
	// Bitmask of connected sides (CoverSide ordinal bits). All set bits lie on
	// a SINGLE axis (straight-only).
	public byte Open { get; init; }
}

// Side <-> Node-bit helpers + the straight-only placement rule for laser pipes.
public static class LaserConn
{
	public static int Bit(IODirection side) => side switch
	{
		IODirection.Up    => 1 << 0,
		IODirection.Down  => 1 << 1,
		IODirection.Left  => 1 << 2,
		IODirection.Right => 1 << 3,
		_                 => 0,
	};

	public static readonly (IODirection side, int dx, int dy)[] Sides =
	{
		(IODirection.Up,   0, -1),
		(IODirection.Down, 0,  1),
		(IODirection.Left, -1, 0),
		(IODirection.Right, 1, 0),
	};

	// Mirror of LaserPipeBlockEntity.setConnection: a laser pipe may only open
	// connections on ONE axis. The first connectable pipe-neighbour picks the
	// axis; a perpendicular neighbour is rejected (no turn/cross). Reciprocal,
	// pipe-to-pipe only (endpoints found by the walker / drawn by the renderer).
	public static void ConnectOnPlace(LaserPipeLayer pipes, int x, int y)
	{
		byte open = 0;
		foreach (var (side, dx, dy) in Sides)
		{
			int nx = x + dx, ny = y + dy;
			var nc = pipes.CellAt(nx, ny);
			if (nc is null) continue;                              // pipe-to-pipe only
			int axis = Bit(side) | Bit(side.Opposite());
			if ((open & ~axis) != 0) continue;                     // this pipe would bend -> reject
			if ((nc.Value.Open & ~axis) != 0) continue;            // neighbour would bend -> reject
			open |= (byte)Bit(side);
			pipes.Set(nx, ny, nc.Value with { Open = (byte)(nc.Value.Open | Bit(side.Opposite())) });
		}
		pipes.Set(x, y, new LaserPipeCell { Open = open });
	}

	public static void ClearOnRemove(LaserPipeLayer pipes, int x, int y)
	{
		foreach (var (side, dx, dy) in Sides)
		{
			int nx = x + dx, ny = y + dy;
			var nc = pipes.CellAt(nx, ny);
			if (nc is null) continue;
			byte cleared = (byte)(nc.Value.Open & ~Bit(side.Opposite()));
			if (cleared != nc.Value.Open)
				pipes.Set(nx, ny, nc.Value with { Open = cleared });
		}
	}
}
