#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Item counterpart of AdjacentFluidPush. Single-item-per-tick matches upstream
// autoOutput's small extractItem ceiling so fast producers drain gradually.
public static class AdjacentItemPush
{
	public static int Push(MetaMachine source, int sourceSlotStart, int sourceSlotCount,
		int maxPerSlot = 1, IODirection side = IODirection.None)
	{
		if (source is not IItemHandler outHandler) return 0;
		return Push(source, outHandler, sourceSlotStart, sourceSlotCount, maxPerSlot, side);
	}

	// Explicit-handler overload for proxy/multi-handler parts (e.g. CokeOvenHatch
	// where the facade's slot indices map to the input side, but auto-output
	// must push from the OUTPUT proxy).
	public static int Push(MetaMachine source, IItemHandler outHandler,
		int sourceSlotStart, int sourceSlotCount,
		int maxPerSlot = 1, IODirection side = IODirection.None)
	{
		int transferred = 0;

		for (int s = sourceSlotStart; s < sourceSlotStart + sourceSlotCount; s++)
		{
			var available = outHandler.Extract(s, maxPerSlot, simulate: true);
			if (available is null || available.IsAir || available.stack <= 0) continue;

			foreach (var (x, y, srcSide) in AdjacentFluidPush.EnumerateAdjacentCells(source, side))
			{
				if (!source.GetItemCapFilter(srcSide, IO.OUT)(available))
					continue;
				var dest = WorldCapability.ItemHandlerAt(x, y, srcSide.Opposite());
				if (dest is null || ReferenceEquals(dest, outHandler)) continue;

				bool insertedAny = false;
				for (int ds = 0; ds < dest.SlotCount; ds++)
				{
					if (!dest.IsItemValid(ds, available)) continue;
					var leftover = dest.Insert(ds, available, simulate: true);
					int wouldInsert = available.stack - (leftover?.stack ?? 0);
					if (wouldInsert <= 0) continue;

					var actuallyExtracted = outHandler.Extract(s, wouldInsert, simulate: false);
					if (actuallyExtracted.IsAir) break;
					dest.Insert(ds, actuallyExtracted, simulate: false);
					transferred += actuallyExtracted.stack;
					insertedAny = true;
					break;
				}
				if (insertedAny) break;
			}
		}
		return transferred;
	}
}
