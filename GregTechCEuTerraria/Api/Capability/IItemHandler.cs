#nullable enable
using Terraria;

namespace GregTechCEuTerraria.Api.Capability;

// Generic contract for any container holding items in indexed slots.
// Wraps vanilla Item[] arrays (chests, player inventory, machine fuel slots)
// or custom data. Lets pipes / machines / GUIs talk to any container without
// caring whether it's a vanilla chest, a modded chest, or a machine slot.
//
// Symmetrical model: Insert returns the leftover that didn't fit; Extract
// returns the items that came out. simulate=true performs the calculation
// without mutating - used by "can this move?" pre-checks.
public interface IItemHandler
{
	int SlotCount { get; }

	// Returns the item in the slot. May be an empty Item (Item.IsAir == true).
	// Callers MUST NOT mutate the returned item directly - go through Insert/Extract.
	Item GetSlot(int slot);

	// Try to insert; returns leftover that didn't fit. If the whole stack fit,
	// returns an empty Item. simulate=true means calculate without mutating.
	Item Insert(int slot, Item item, bool simulate);

	// Try to extract up to maxAmount items from slot. Returns what came out
	// (may be empty if slot is empty or filter rejects). simulate=true means
	// calculate without mutating.
	Item Extract(int slot, int maxAmount, bool simulate);

	// Per-slot stack-size cap. Mirrors Forge's `getSlotLimit(int slot)`.
	// Default = Item.CommonMaxStack (Terraria's universal item.maxStack
	// ceiling). Override for slots that cap below the item's natural max
	// (e.g. output slots that should only hold one cycle's worth).
	int GetSlotLimit(int slot) => Item.CommonMaxStack;

	// Per-slot filter. Default implementation accepts any item; override for
	// e.g. a fuel-only slot.
	bool IsItemValid(int slot, Item item) => true;
}

// Mirrors Forge's `IItemHandlerModifiable extends IItemHandler` - adds a
// SetSlot that lets external code REPLACE a slot's contents (NBT load,
// test fixtures, packet sync). Implementations that allow this expose it
// via the sub-interface; pipes / GUIs that only insert/extract use the
// base IItemHandler.
public interface IItemHandlerModifiable : IItemHandler
{
	void SetSlot(int slot, Item item);
}
