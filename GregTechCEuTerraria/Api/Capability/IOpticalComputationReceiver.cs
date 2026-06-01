#nullable enable
namespace GregTechCEuTerraria.Api.Capability;

// Port of com.gregtechceu.gtceu.api.capability.IOpticalComputationReceiver.
//
// Marker for a machine that CONSUMES computation but doesn't itself supply
// it - exposes the provider it relies on via `GetComputationProvider()`.
// Used in conjunction with `NotifiableComputationContainer` so the trait
// knows where to forward CWU/t requests.
public interface IOpticalComputationReceiver
{
	IOpticalComputationProvider? GetComputationProvider();
}
