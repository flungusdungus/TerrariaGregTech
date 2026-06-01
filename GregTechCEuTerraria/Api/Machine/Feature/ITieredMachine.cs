#nullable enable
namespace GregTechCEuTerraria.Api.Machine.Feature;

// LOCKED - verbatim port of com.gregtechceu.gtceu.api.machine.feature.
// ITieredMachine.
//
// Marker contract: a machine that has a voltage tier. Read by recipe modifiers
// (overclock, tiered recipe filters) and by UI tooltips.
public interface ITieredMachine
{
	int GetTier();
}
