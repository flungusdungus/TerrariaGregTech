#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;

namespace GregTechCEuTerraria.Api.Machine.Trait;

// Port of com.gregtechceu.gtceu.api.machine.trait.ICapabilityTrait.
//
// Tiny marker for a trait that declares an IO direction. `NotifiableEnergy
// Container` / `NotifiableItemStackHandler` / `NotifiableFluidTank` all
// implement this so the part walker (`MultiblockPartMachine.GetHandlerList`)
// can ask each trait which direction it points without knowing its concrete
// type.
public interface ICapabilityTrait
{
	IO GetCapabilityIO();

	bool CanCapInput()  => GetCapabilityIO().Supports(IO.IN);
	bool CanCapOutput() => GetCapabilityIO().Supports(IO.OUT);
}
