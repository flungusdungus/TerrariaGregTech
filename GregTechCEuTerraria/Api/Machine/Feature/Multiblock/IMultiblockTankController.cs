#nullable enable
using GregTechCEuTerraria.Api.Machine.Trait;

namespace GregTechCEuTerraria.Api.Machine.Feature.Multiblock;

// Forward-decl interface for any multiblock controller that exposes a
// single bound `NotifiableFluidTank` (i.e. the GT large-tank multi).
// `TankValvePartMachine` checks `controller is IMultiblockTankController`
// to bind its `FluidTankProxyTrait`'s `Proxy` to the controller's tank.
//
// Upstream uses a direct `controller instanceof MultiblockTankMachine`
// class check; in C# the cleaner shape is a feature interface, which the
// concrete `MultiblockTankMachine` (when ported) implements. Same semantic.
public interface IMultiblockTankController
{
	NotifiableFluidTank GetTank();
}
