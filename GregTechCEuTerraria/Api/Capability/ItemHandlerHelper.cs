#nullable enable
using Terraria;

namespace GregTechCEuTerraria.Api.Capability;

// Adaptation of Forge's `net.minecraftforge.items.ItemHandlerHelper` - the
// stack-aware whole-handler insert/extract used by upstream covers + pipe
// routing. Only the subset our port currently needs lives here; expand as
// new callsites land.
public static class ItemHandlerHelper
{
	// Verbatim of upstream `ItemHandlerHelper.insertItemStacked`: fill
	// matching-type slots first (so partial stacks consolidate), then
	// empty slots. Returns the leftover stack (Item.IsAir when fully
	// consumed). When `simulate=true` no slot is actually written.
	public static Item InsertItemStacked(IItemHandler dest, Item stack, bool simulate)
	{
		if (stack.IsAir) return new Item();
		Item remaining = stack.Clone();
		for (int i = 0; i < dest.SlotCount; i++)
		{
			Item slot = dest.GetSlot(i);
			if (!slot.IsAir && slot.type == remaining.type)
			{
				remaining = dest.Insert(i, remaining, simulate);
				if (remaining.IsAir) return new Item();
			}
		}
		for (int i = 0; i < dest.SlotCount; i++)
		{
			if (dest.GetSlot(i).IsAir)
			{
				remaining = dest.Insert(i, remaining, simulate);
				if (remaining.IsAir) return new Item();
			}
		}
		return remaining;
	}
}
