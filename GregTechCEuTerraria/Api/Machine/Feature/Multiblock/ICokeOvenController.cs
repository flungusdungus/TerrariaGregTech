#nullable enable
using GregTechCEuTerraria.Api.Machine.Trait;

namespace GregTechCEuTerraria.Api.Machine.Feature.Multiblock;

// Forward-decl for the Coke Oven primitive multi controller. Exposes its
// three trait-backed handlers so `CokeOvenHatch` can rebind its proxies
// on `AddedToController`. Upstream is a direct `controller instanceof
// CokeOvenMachine` class check; in C# the feature-interface shape is
// cleaner - the concrete `CokeOvenMachine` (when ported) implements this.
public interface ICokeOvenController
{
	NotifiableItemStackHandler ImportItems  { get; }
	NotifiableItemStackHandler ExportItems  { get; }
	NotifiableFluidTank        ExportFluids { get; }
}
