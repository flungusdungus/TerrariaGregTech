#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Identifies an Item[] collection on a MetaMachine for SlotAction routing.
// Ordinals are wire-protocol - add new values at the END only.
public enum SlotGroup : byte
{
	// R/O combined view (input+output concat). Mutations MUST use
	// InventoryInput/InventoryOutput to round-trip via the trait backing array.
	Inventory = 0,

	// Tier-matched battery charger (SimpleTieredMachine shape).
	Charger   = 1,

	// Per-direction handler arrays - separate trait per direction; ref-mutations
	// land on the trait's array.
	InventoryInput  = 2,
	InventoryOutput = 3,

	// RotorHolder's single rotor slot (IO.NONE, recipe-routing-exempt).
	RotorSlot       = 4,
}
