#nullable enable
namespace GregTechCEuTerraria.Api.Capability;

// Port of com.gregtechceu.gtceu.api.capability.IOpticalDataAccessHatch.
//
// Extends `IDataAccessHatch` to mark hatches that transmit/receive research
// data through optical pipes (vs. local data-stick storage).
public interface IOpticalDataAccessHatch : IDataAccessHatch
{
	// True = transmits data through cables (server-side originator);
	// false = receives.
	bool IsTransmitter();
}
