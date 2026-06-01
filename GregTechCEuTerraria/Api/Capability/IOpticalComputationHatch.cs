#nullable enable
namespace GregTechCEuTerraria.Api.Capability;

// Port of com.gregtechceu.gtceu.api.capability.IOpticalComputationHatch.
//
// Marker for an optical computation hatch - transmitter (sends CWU/t out)
// or receiver (accepts CWU/t in). Extends `IOpticalComputationProvider`
// so both kinds plug into the same provider lookup chain.
public interface IOpticalComputationHatch : IOpticalComputationProvider
{
	// True = transmitter (output side), False = receiver (input side).
	bool IsTransmitter();
}
