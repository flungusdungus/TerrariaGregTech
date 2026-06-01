#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Pull side of AdjacentItemPush. Shape matches upstream
// NotifiableItemStackHandler.importFromNearby(side). Used by multiblock input
// buses; the non-multi processing machines never auto-pull upstream either
// (input comes via ConveyorCover / pipes).
public static class AdjacentItemPull
{
	// side=None scans full perimeter; exclude drops one cardinal from a scan.
	public static int Pull(MetaMachine source, int destSlotStart, int destSlotCount,
		int maxPerSlot = int.MaxValue, IODirection side = IODirection.None,
		IODirection exclude = IODirection.None)
	{
		if (source is not IItemHandler ourHandler) return 0;
		if (destSlotCount <= 0) return 0;
		int transferred = 0;

		foreach (var (x, y, srcSide) in AdjacentFluidPush.EnumerateAdjacentCells(source, side, exclude))
		{
			var ourFilter = source.GetItemCapFilter(srcSide, IO.IN);

			var srcHandler = WorldCapability.ItemHandlerAt(x, y, srcSide.Opposite());
			if (srcHandler is null || ReferenceEquals(srcHandler, ourHandler)) continue;

			for (int srcSlot = 0; srcSlot < srcHandler.SlotCount; srcSlot++)
			{
				var available = srcHandler.Extract(srcSlot, maxPerSlot, simulate: true);
				if (available is null || available.IsAir || available.stack <= 0) continue;
				if (!ourFilter(available)) continue;

				for (int destSlot = destSlotStart; destSlot < destSlotStart + destSlotCount; destSlot++)
				{
					if (!ourHandler.IsItemValid(destSlot, available)) continue;
					var leftover = ourHandler.Insert(destSlot, available, simulate: true);
					int wouldInsert = available.stack - (leftover?.stack ?? 0);
					if (wouldInsert <= 0) continue;

					var actuallyExtracted = srcHandler.Extract(srcSlot, wouldInsert, simulate: false);
					if (actuallyExtracted.IsAir) break;
					ourHandler.Insert(destSlot, actuallyExtracted, simulate: false);
					transferred += actuallyExtracted.stack;
					break;
				}
			}
		}
		return transferred;
	}
}
