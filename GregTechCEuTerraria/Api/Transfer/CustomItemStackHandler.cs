#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Transfer;

// LOCKED - port of
// com.gregtechceu.gtceu.api.transfer.item.CustomItemStackHandler.
//
// Slot-backed item store with content-change callback + per-slot filter +
// drop-on-destroy helper. Upstream extends Forge's `ItemStackHandler`; we
// reproduce its surface inline since we don't have Forge.
//
// Documented adaptations:
//   - Forge ItemStack / NBT -> Terraria Item / TagCompound. Item.IsAir replaces
//     ItemStack.isEmpty; Item.Clone() replaces ItemStack.copy.
//   - INBTSerializable<CompoundTag> -> SaveData/LoadData(TagCompound) pair.
//   - dropInventoryInWorld uses Item.NewItem instead of Block.popResource.
public class CustomItemStackHandler : IItemHandlerModifiable
{
	public Item[] Stacks;

	// Content-change notifier. Trait subscribers (RecipeLogic, sync packets)
	// hook this to know when to re-search recipes / mark sync dirty.
	public Action OnContentsChangedAction { get; set; } = () => { };

	// Per-stack filter. Default accepts any item; subclasses set it to gate
	// what fits in a slot (fuel-only slot, output-only-when-allowed, etc.).
	public Predicate<Item> Filter { get; set; } = _ => true;

	public CustomItemStackHandler() { Stacks = System.Array.Empty<Item>(); }

	public CustomItemStackHandler(int size)
	{
		Stacks = new Item[size];
		for (int i = 0; i < size; i++) Stacks[i] = new Item();
	}

	public CustomItemStackHandler(Item itemStack) : this(1)
	{
		Stacks[0] = itemStack ?? new Item();
	}

	public CustomItemStackHandler(IList<Item> stacks)
	{
		Stacks = new Item[stacks.Count];
		for (int i = 0; i < Stacks.Length; i++) Stacks[i] = stacks[i] ?? new Item();
	}

	// === IItemHandlerModifiable surface =====================================

	public int SlotCount => Stacks.Length;
	public int GetSlots() => Stacks.Length;

	public Item GetSlot(int slot) => Stacks[slot];
	public Item GetStackInSlot(int slot) => Stacks[slot];

	public void SetSlot(int slot, Item item) => SetStackInSlot(slot, item);

	public virtual void SetStackInSlot(int slot, Item stack)
	{
		Stacks[slot] = stack ?? new Item();
		OnContentsChanged(slot);
	}

	public virtual bool IsItemValid(int slot, Item stack) => Filter(stack);

	public virtual int GetSlotLimit(int slot) => Item.CommonMaxStack;

	// Mirrors upstream's ItemStackHandler.insertItem.
	public virtual Item Insert(int slot, Item item, bool simulate)
	{
		if (item is null || item.IsAir) return new Item();
		if (!IsItemValid(slot, item)) return item.Clone();

		var existing = Stacks[slot];
		int limit = GetStackLimit(slot, item);

		if (existing.IsAir)
		{
			int accept = Math.Min(item.stack, limit);
			var leftover = item.Clone();
			leftover.stack = item.stack - accept;
			if (leftover.stack <= 0) leftover.TurnToAir();
			if (!simulate)
			{
				var placed = item.Clone();
				placed.stack = accept;
				Stacks[slot] = placed;
				OnContentsChanged(slot);
			}
			return leftover;
		}

		// Same-type stack - top up. Must ALSO pass ItemLoader.CanStack so per-stack
		// GlobalItem/ModItem differences are honored (e.g. a researched data orb
		// never merges with a different research - ResearchDataGlobalItem.CanStack);
		// CanStack checks prefix + the CanStack hooks but NOT type, so we AND it
		// with the type check.
		if (existing.type == item.type &&
		    Terraria.ModLoader.ItemLoader.CanStack(existing, item))
		{
			int room = limit - existing.stack;
			if (room <= 0) return item.Clone();
			int accept = Math.Min(item.stack, room);
			var leftover = item.Clone();
			leftover.stack = item.stack - accept;
			if (leftover.stack <= 0) leftover.TurnToAir();
			if (!simulate)
			{
				existing.stack += accept;
				OnContentsChanged(slot);
			}
			return leftover;
		}

		return item.Clone();
	}

	// Mirrors upstream's ItemStackHandler.extractItem.
	public virtual Item Extract(int slot, int maxAmount, bool simulate)
	{
		if (maxAmount <= 0) return new Item();
		var existing = Stacks[slot];
		if (existing.IsAir) return new Item();
		int take = Math.Min(existing.stack, maxAmount);
		var out_ = existing.Clone();
		out_.stack = take;
		if (!simulate)
		{
			int rem = existing.stack - take;
			if (rem <= 0)
			{
				Stacks[slot] = new Item();
			}
			else
			{
				existing.stack = rem;
			}
			OnContentsChanged(slot);
		}
		return out_;
	}

	// Slot-specific stack limit - clamp by both filter cap and item natural cap.
	protected virtual int GetStackLimit(int slot, Item stack) =>
		Math.Min(GetSlotLimit(slot), stack.maxStack);

	// Called from the various mutators above. Subclasses can override the
	// virtual overload for per-slot reactions; the parameterless variant
	// fires the OnContentsChangedAction.
	protected virtual void OnContentsChanged(int slot) => OnContentsChangedAction();

	public virtual void Clear()
	{
		for (int i = 0; i < Stacks.Length; i++) Stacks[i] = new Item();
		OnContentsChangedAction();
	}

	// Drop every item to the world at the given tile position. Used by the
	// NotifiableItemStackHandler trait's onMachineDestroyed.
	public void DropInventoryInWorld(int tileX, int tileY)
	{
		int wx = tileX * 16 + 8;
		int wy = tileY * 16 + 8;
		foreach (var stack in Stacks)
		{
			if (stack.IsAir) continue;
			Terraria.Item.NewItem(null, wx, wy, 16, 16, stack.type, stack.stack);
		}
		Clear();
	}

	// === Persistence ========================================================

	public TagCompound SerializeNBT()
	{
		var tag = new TagCompound();
		var items = new List<TagCompound>(Stacks.Length);
		foreach (var s in Stacks) items.Add(ItemIO.Save(s));
		tag["items"] = items;
		tag["size"]  = Stacks.Length;
		return tag;
	}

	public void DeserializeNBT(TagCompound tag)
	{
		int size = tag.ContainsKey("size") ? tag.GetInt("size") : Stacks.Length;
		if (Stacks.Length != size)
		{
			Stacks = new Item[size];
			for (int i = 0; i < size; i++) Stacks[i] = new Item();
		}
		if (tag.ContainsKey("items"))
		{
			var items = tag.GetList<TagCompound>("items");
			for (int i = 0; i < items.Count && i < Stacks.Length; i++)
				Stacks[i] = ItemIO.Load(items[i]);
		}
	}
}
