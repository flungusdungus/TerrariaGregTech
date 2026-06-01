#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Data.Chemical.Material;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Fluids.Store;
using GregTechCEuTerraria.Api.Fluids;
using System;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Capabilities.Handlers;

// IItemHandler over a backing Item[] array - works for vanilla chests, the
// player inventory, machine slots, anywhere items live in an indexed array.
// The caller owns the array; we just project an IItemHandler view onto it.
//
// Optional perSlotFilter lets callers reject items per-slot (e.g. a fuel slot
// only accepts items with burnTime > 0). Pass null to accept anything.
public sealed class ArrayItemHandler : IItemHandler
{
	private readonly Item[] _slots;
	private readonly Func<int, Item, bool>? _filter;

	public ArrayItemHandler(Item[] slots, Func<int, Item, bool>? filter = null)
	{
		_slots = slots;
		_filter = filter;
	}

	public int SlotCount => _slots.Length;

	public Item GetSlot(int slot) => _slots[slot];

	public bool IsItemValid(int slot, Item item) =>
		_filter is null || _filter(slot, item);

	public Item Insert(int slot, Item item, bool simulate)
	{
		if (item is null || item.IsAir) return new Item();
		if (!IsItemValid(slot, item)) return item.Clone();

		var existing = _slots[slot];

		// Empty target slot - drop the whole stack in (capped by maxStack).
		if (existing is null || existing.IsAir)
		{
			int accept = Math.Min(item.stack, item.maxStack);
			var leftover = item.Clone();
			leftover.stack = item.stack - accept;
			if (leftover.stack <= 0) leftover.TurnToAir();

			if (!simulate)
			{
				var placed = item.Clone();
				placed.stack = accept;
				_slots[slot] = placed;
			}
			return leftover;
		}

		// Occupied target - must be same item to stack.
		if (existing.type != item.type) return item.Clone();

		int max = Math.Min(existing.maxStack, item.maxStack);
		int room = max - existing.stack;
		if (room <= 0) return item.Clone();

		int merged = Math.Min(room, item.stack);
		var leftoverStack = item.Clone();
		leftoverStack.stack = item.stack - merged;
		if (leftoverStack.stack <= 0) leftoverStack.TurnToAir();

		if (!simulate)
			existing.stack += merged;
		return leftoverStack;
	}

	public Item Extract(int slot, int maxAmount, bool simulate)
	{
		var existing = _slots[slot];
		if (existing is null || existing.IsAir || maxAmount <= 0) return new Item();

		int take = Math.Min(existing.stack, maxAmount);
		var taken = existing.Clone();
		taken.stack = take;

		if (!simulate)
		{
			existing.stack -= take;
			if (existing.stack <= 0) existing.TurnToAir();
		}
		return taken;
	}
}
