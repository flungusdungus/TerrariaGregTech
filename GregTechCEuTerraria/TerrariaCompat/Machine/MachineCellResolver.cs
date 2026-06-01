#nullable enable
using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Resolves any cell of a multi-tile machine back to the entity at its origin.
// TileFrameX/Y encode the sub-tile quadrant; entity lives at the top-left.
public static class MachineCellResolver
{
	public static bool TryFindMachineAt(int i, int j, out MetaMachine machine)
	{
		machine = null!;
		if (i < 0 || i >= Main.maxTilesX || j < 0 || j >= Main.maxTilesY) return false;
		Tile tile = Main.tile[i, j];
		if (!tile.HasTile) return false;

		// tML 18 px stride (16 content + 2 padding); 2x2 yields {0,18}.
		int dx = (tile.TileFrameX / 18) % 8;
		int dy = (tile.TileFrameY / 18) % 8;
		int originX = i - dx;
		int originY = j - dy;

		if (!TileEntity.ByPosition.TryGetValue(new Point16(originX, originY), out var te))
			return false;
		if (te is MetaMachine m)
		{
			machine = m;
			return true;
		}
		return false;
	}

	// Per-cell cardinal walk; naive Position+Cardinal4 lands inside the
	// footprint on a 2x2. Deduped - multi-cell neighbour yielded once.
	public static IEnumerable<(IODirection side, MetaMachine neighbor)> PerimeterNeighbors(MetaMachine machine)
	{
		var own = new HashSet<(int, int)>(machine.Cells());
		var seen = new HashSet<MetaMachine>();
		foreach (var (cx, cy) in machine.Cells())
		{
			foreach (var (side, dx, dy) in MachineSides.Cardinal4)
			{
				int nx = cx + dx, ny = cy + dy;
				if (own.Contains((nx, ny))) continue;
				if (!TryFindMachineAt(nx, ny, out var neighbor)) continue;
				if (ReferenceEquals(neighbor, machine)) continue;
				if (seen.Add(neighbor))
					yield return (side, neighbor);
			}
		}
	}

	public static bool TryFindAt<T>(int i, int j, out T machine) where T : MetaMachine
	{
		if (TryFindMachineAt(i, j, out var any) && any is T t)
		{
			machine = t;
			return true;
		}
		machine = null!;
		return false;
	}
}
