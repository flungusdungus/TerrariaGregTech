#nullable enable
using System;
using Terraria;

namespace GregTechCEuTerraria.Api.Cover.Filter;

// Verbatim PhantomSlotWidget.slotClickPhantom - the item-phantom click math.
// A phantom slot holds a "ghost" item (type + a configured count), never a real
// stack:
//   LMB with a held item  -> set the slot to the held item (count = held count)
//   RMB with a held item  -> set the slot to the held item (count = 1)
//   empty-handed LMB / RMB -> step the slot's count -1 / +1
//   Shift                 -> halve / double instead of +/-1
//   middle-click          -> clear the slot
//
// Shared by the cover-filter edit path (CoverFilterAction, server-authoritative)
// and the item-magnet filter UI (UIMagnetPhantomSlot, the magnet is a private
// inventory item - edited client-side). The held item is read-only - phantom
// slots never consume it.
public static class ItemFilterEdit
{
	public static void MatcherClick(SimpleItemFilter filter, int index, int button, bool shift, Item held)
	{
		if (index < 0 || index >= filter.Matches.Length) return;
		Item slot = filter.Matches[index];

		if (button == 2)
			filter.Matches[index] = new Item();
		else if (button == 0 || button == 1)
		{
			if (slot.IsAir)
			{
				if (!held.IsAir) filter.Matches[index] = Fill(held, button, filter.MaxStackSize);
			}
			else if (held.IsAir)
				filter.Matches[index] = Adjust(slot, button, shift, filter.MaxStackSize);
			else
				// Upstream adjusts the old stack here then immediately overwrites
				// it; the adjust has no observable effect - we just fill.
				filter.Matches[index] = Fill(held, button, filter.MaxStackSize);
		}
		filter.OnUpdated();
	}

	private static Item Fill(Item held, int button, int maxStack)
	{
		int count = Math.Clamp(button == 0 ? held.stack : 1, 1, Math.Min(maxStack, held.maxStack));
		var s = held.Clone();
		s.stack = count;
		return s;
	}

	private static Item Adjust(Item slot, int button, bool shift, int maxStack)
	{
		int cur = slot.stack;
		int next = shift ? (button == 0 ? (cur + 1) / 2 : cur * 2)
		                 : (button == 0 ? cur - 1 : cur + 1);
		next = Math.Min(next, Math.Min(maxStack, slot.maxStack));
		if (next <= 0) return new Item();
		var s = slot.Clone();
		s.stack = next;
		return s;
	}
}
