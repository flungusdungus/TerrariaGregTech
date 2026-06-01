#nullable enable
namespace GregTechCEuTerraria.Api.Capability;

// LOCKED - verbatim port of com.gregtechceu.gtceu.api.capability.IElectricItem.
// DO NOT modify behavior; only mirror upstream changes.
//
// Per-stack electric-item contract: what every rechargeable battery / power
// cell / nano-saber implements. Upstream's concrete implementation lives in
// api/item/capability/ElectricItem.java; our BatteryItem implements this
// interface and inlines the math (no separate capability backing object -
// Terraria items don't have Forge's per-stack capability layer).
public interface IElectricItem
{
	// True if this item can be inserted into discharge slots (battery buffers,
	// chargers). False for items that hold energy for their own use only
	// (electric tools, nano-saber).
	bool CanProvideChargeExternally();

	// True if this item is rechargeable. False for single-use cells.
	bool Chargeable();

	// Charge this item with `amount` EU. Bounded by capacity, by transfer
	// limit (unless ignoreTransferLimit), and by tier (chargerTier must be
	// >= this.Tier - matches upstream's strict tier gate in ElectricItem).
	//
	// Returns EU actually transferred IN.
	long Charge(long amount, int chargerTier, bool ignoreTransferLimit, bool simulate);

	// Discharge this item by `amount` EU. Bounded by current charge, by
	// transfer limit, and by tier.
	// `externally` is the upstream knob that prevents non-battery items from
	// powering things they shouldn't (e.g. a nano-saber's internal EU
	// shouldn't be pulled by a battery buffer slot - externally=true).
	//
	// Returns EU actually transferred OUT.
	long Discharge(long amount, int dischargerTier, bool ignoreTransferLimit, bool externally, bool simulate);

	// Max EU/t for charge or discharge.
	long GetTransferLimit();

	// Storage capacity in EU.
	long GetMaxCharge();

	// Currently-stored EU.
	long GetCharge();

	// Verbatim default from upstream: simulate a tier-MAX, no-limit discharge
	// of exactly `amount` and see if we got the full request.
	bool CanUse(long amount) =>
		Discharge(amount, int.MaxValue, ignoreTransferLimit: true, externally: false, simulate: true) == amount;

	// Voltage tier of this item (index into the V[] table - same tier system
	// as machines / cables).
	int GetTier();
}
