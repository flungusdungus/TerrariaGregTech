#nullable enable
namespace GregTechCEuTerraria.Api.Capability;

// Port of com.gregtechceu.gtceu.api.capability.ILaserContainer.
//
// Marker sub-interface of `IEnergyContainer` - laser cables / hatches use
// it to route exclusively to other laser endpoints (regular EU pipes don't
// connect). Upstream uses this purely to keep "laser piping" separable from
// normal energy piping; no behavioural difference at the contract level.
//
// Note: the runtime laser-cable pipeline (INPUT_LASER / OUTPUT
// _LASER part abilities + a laser-cable layer) is not yet ported. This
// interface is the substrate; `NotifiableLaserContainer` is the trait.
// Multis using laser hatches will register and form correctly; energy
// transfer through laser cables is a no-op until that layer lands.
public interface ILaserContainer : IEnergyContainer
{
}
