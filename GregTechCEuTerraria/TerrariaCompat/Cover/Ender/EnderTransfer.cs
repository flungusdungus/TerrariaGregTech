#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover.Filter;
using GregTechCEuTerraria.Api.Fluids;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Ender;

// Filtered, rate-limited transfer helpers for the ender link covers - the
// Terraria-side stand-in for upstream's GTTransferUtils.transferItemsFiltered /
// transferFluidsFiltered.
public static class EnderTransfer
{
	// Move up to `max` filter-passing items from `from` into `to`.
	public static int TransferItems(IItemHandler from, IItemHandler to, IItemFilter filter, int max)
	{
		int moved = 0;
		for (int s = 0; s < from.SlotCount && moved < max; s++)
		{
			var probe = from.Extract(s, max - moved, simulate: true);
			if (probe.IsAir || !filter.Test(probe)) continue;

			var leftover = InsertAcrossSlots(to, probe, simulate: true);
			int canMove = probe.stack - leftover.stack;
			if (canMove <= 0) continue;

			var taken = from.Extract(s, canMove, simulate: false);
			if (taken.IsAir) continue;
			InsertAcrossSlots(to, taken, simulate: false);
			moved += taken.stack;
		}
		return moved;
	}

	// Insert a stack across all of dest's slots; returns the leftover (air if
	// it all fit). Mirror of Forge ItemHandlerHelper.insertItem.
	private static Item InsertAcrossSlots(IItemHandler dest, Item stack, bool simulate)
	{
		var remaining = stack.Clone();
		for (int i = 0; i < dest.SlotCount; i++)
		{
			remaining = dest.Insert(i, remaining, simulate);
			if (remaining.IsAir) return new Item();
		}
		return remaining;
	}

	// Move up to `max` mB of filter-passing fluid from `from` into `to`.
	public static int TransferFluids(IFluidHandler from, IFluidHandler to, IFluidFilter filter, int max)
	{
		if (max <= 0) return 0;
		var probe = from.Drain(max, simulate: true);
		if (probe.IsEmpty || !filter.Test(probe)) return 0;

		int fillable = to.Fill(probe, simulate: true);
		if (fillable <= 0) return 0;

		var drained = from.Drain(fillable, simulate: false);
		if (drained.IsEmpty) return 0;
		return to.Fill(drained, simulate: false);
	}
}
