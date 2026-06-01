#nullable enable
using GregTechCEuTerraria.Common.Energy;

namespace GregTechCEuTerraria.Api.Machine.Feature;

// Port of com.gregtechceu.gtceu.api.machine.feature.IOverclockMachine.
//
// A machine that overclocks recipes. The ELECTRIC_OVERCLOCK recipe modifier
// downcasts to this to read the machine's overclock voltage / tier cap.
public interface IOverclockMachine
{
	int OverclockTier { get; }

	void SetOverclockTier(int tier);

	int MaxOverclockTier { get; }

	int MinOverclockTier { get; }

	// Upstream default: GTValues.V[getOverclockTier()].
	long OverclockVoltage => VoltageTiers.Voltage((VoltageTier)OverclockTier);
}
