#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Optical;

// One placed optical pipe. Unlike laser (straight-line only), optical pipes
// allow bends but forbid splitting: upstream OpticalPipeBlockEntity.setConnection
// rejects a connection once the pipe already has 2 (getNumConnections() >= 2).
// We mirror that by storing the per-side open-connection bitmask on the cell:
// at most 2 bits set, computed reciprocally with pipe neighbours at placement.
// The mask uses CoverSide ordinals (Up=0,Down=1,Left=2,Right=3) so it can feed
// straight into Node.OpenConnections, and the net's CanNodesConnect then forms
// non-branching paths instead of merging every adjacent pipe.
public readonly record struct OpticalPipeCell
{
	// Bitmask of connected sides (CoverSide ordinal bits). <=2 bits set.
	public byte Open { get; init; }
}

// Side <-> Node-bit helpers shared by the layer / handle / walker / net.
// IODirection (Up=1..Right=4) does NOT match the CoverSide ordinals used by
// Node.OpenConnections (Up=0..Right=3), so the mapping is explicit.
public static class OpticalConn
{
	public static int Bit(IODirection side) => side switch
	{
		IODirection.Up    => 1 << 0,
		IODirection.Down  => 1 << 1,
		IODirection.Left  => 1 << 2,
		IODirection.Right => 1 << 3,
		_                 => 0,
	};

	public static int PopCount(byte open) => System.Numerics.BitOperations.PopCount(open);

	public static readonly (IODirection side, int dx, int dy)[] Sides =
	{
		(IODirection.Up,   0, -1),
		(IODirection.Down, 0,  1),
		(IODirection.Left, -1, 0),
		(IODirection.Right, 1, 0),
	};

	// Mirror of OpticalPipeBlockEntity.setConnection at placement: open up to 2
	// sides toward adjacent optical pipes, reciprocally, skipping any side whose
	// neighbour already has 2 connections. Sets the placed cell's mask + the
	// connected neighbours' opposite bits. Pipe-to-pipe only (endpoints are
	// found by the walker scanning all sides; not counted toward the 2 - a
	// documented simplification that avoids upstream's order-dependent
	// neighbour-change recompute while still forbidding pipe splitting/cycles).
	public static void ConnectOnPlace(OpticalPipeLayer pipes, int x, int y)
	{
		byte open = 0;
		foreach (var (side, dx, dy) in Sides)
		{
			if (PopCount(open) >= 2) break;
			int nx = x + dx, ny = y + dy;
			var nc = pipes.CellAt(nx, ny);
			if (nc is null) continue;                       // pipe-to-pipe only
			if (PopCount(nc.Value.Open) >= 2) continue;     // neighbour full
			open |= (byte)Bit(side);
			pipes.Set(nx, ny, nc.Value with { Open = (byte)(nc.Value.Open | Bit(side.Opposite())) });
		}
		pipes.Set(x, y, new OpticalPipeCell { Open = open });
	}

	// On removal, clear each neighbour pipe's bit that pointed at (x,y), freeing
	// that connection slot.
	public static void ClearOnRemove(OpticalPipeLayer pipes, int x, int y)
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
