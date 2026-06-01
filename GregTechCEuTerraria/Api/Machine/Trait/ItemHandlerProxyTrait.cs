#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;

namespace GregTechCEuTerraria.Api.Machine.Trait;

// Port of com.gregtechceu.gtceu.api.machine.trait.ItemHandlerProxyTrait.
//
// A trait that exposes itself as an `IItemHandlerModifiable` but delegates
// every operation to a settable `Proxy` reference - the actual inventory
// lives somewhere else (typically the multiblock controller's storage).
// Used by `CokeOvenHatch` to surface the controller's item slots as a
// per-face I/O point.
//
// Documented adaptations (forced by codebase architecture):
//   - `Direction... facings` -> `IODirection[] facings` (2D port).
//   - `GTTransferUtils.transferItemsFiltered` -> `AdjacentItemPush.Push`
//     (codebase-wide; same per-side cover filter via Machine.GetItemCap
//     Filter).
public sealed class ItemHandlerProxyTrait : MachineTrait, IItemHandlerModifiable, ICapabilityTrait
{
	public static readonly MachineTraitType<ItemHandlerProxyTrait> TYPE = new(allowMultipleInstances: true);
	public override MachineTraitType TraitType => TYPE;

	public IO                       CapabilityIO { get; }
	public IItemHandlerModifiable?  Proxy { get; set; }

	public ItemHandlerProxyTrait(IO capabilityIO) : base()
	{
		CapabilityIO = capabilityIO;
	}

	// === ICapabilityTrait ====================================================
	public IO   GetCapabilityIO() => CapabilityIO;
	public bool CanCapInput()     => CapabilityIO.Supports(IO.IN);
	public bool CanCapOutput()    => CapabilityIO.Supports(IO.OUT);

	// === IItemHandlerModifiable - direct delegation with capability gating ==

	public int SlotCount => Proxy?.SlotCount ?? 0;

	public Item GetSlot(int slot) =>
		Proxy?.GetSlot(slot) ?? new Item();

	public void SetSlot(int slot, Item item)
	{
		if (Proxy != null) Proxy.SetSlot(slot, item);
	}

	public Item Insert(int slot, Item item, bool simulate)
	{
		if (Proxy != null && CanCapInput()) return Proxy.Insert(slot, item, simulate);
		return item;
	}

	public Item Extract(int slot, int maxAmount, bool simulate)
	{
		if (Proxy != null && CanCapOutput()) return Proxy.Extract(slot, maxAmount, simulate);
		return new Item();
	}

	public int GetSlotLimit(int slot) =>
		Proxy?.GetSlotLimit(slot) ?? 0;

	public bool IsItemValid(int slot, Item item) =>
		Proxy != null && Proxy.IsItemValid(slot, item);

	// === Internal (capability-gate-bypassing) variants - upstream parity ====

	public Item InsertInternal(int slot, Item item, bool simulate) =>
		Proxy?.Insert(slot, item, simulate) ?? item;

	public Item ExtractInternal(int slot, int maxAmount, bool simulate) =>
		Proxy?.Extract(slot, maxAmount, simulate) ?? new Item();

	// === Emptiness check (upstream fast-path for NotifiableItemStackHandler)

	public bool IsEmpty()
	{
		if (Proxy is NotifiableItemStackHandler nish) return nish.IsEmpty();
		if (Proxy == null) return true;
		for (int i = 0; i < Proxy.SlotCount; i++)
			if (!Proxy.GetSlot(i).IsAir) return false;
		return true;
	}

	// === Auto-output helper (upstream parity) ===============================

	public void ExportToNearby(params IODirection[] facings)
	{
		if (IsEmpty() || Proxy == null) return;
		if (Machine is not MetaMachine mm) return;
		// Push from THIS proxy specifically (not via the machine's
		// IItemHandler face) - see the explicit-handler overload's
		// header. CokeOvenHatch in particular needs this: its
		// ExposedItemHandler is a combined facade, not the output
		// proxy alone.
		foreach (var facing in facings)
			AdjacentItemPush.Push(mm, this, 0, Proxy.SlotCount, maxPerSlot: int.MaxValue, side: facing);
	}
}
