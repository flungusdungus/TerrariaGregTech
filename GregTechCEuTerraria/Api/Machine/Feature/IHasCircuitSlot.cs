#nullable enable
namespace GregTechCEuTerraria.Api.Machine.Feature;

// Port of com.gregtechceu.gtceu.api.machine.feature.IHasCircuitSlot.
//
// Marker for any machine that exposes a programmed-circuit slot - used by
// the UI to surface a fancy-configurator widget for the circuit and by the
// IRecipeLogicMachine path to read the configured circuit value when
// matching recipes. Implementers expose the slot's circuit value as a byte
// (0-32) or as the `programmed_circuit` Item itself.
public interface IHasCircuitSlot
{
	// True when the circuit slot is currently exposed (item buses inside a
	// multi that doesn't allow circuit slots flip this off).
	bool IsCircuitSlotEnabled();

	// The 1-slot NotifiableItemStackHandler that holds the configured
	// programmed_circuit item. Mirrors upstream `circuitInventory` field on
	// SimpleTieredMachine + ItemBusPartMachine + FluidHatchPartMachine. The
	// widget (UICircuitButton / GhostCircuitSlotWidget) binds directly to
	// this handler; CircuitSetAction replaces slot 0 with a fresh
	// IntCircuitItem at the configured value (or empty for "no circuit").
	Api.Machine.Trait.NotifiableItemStackHandler? CircuitInventory { get; }
}
