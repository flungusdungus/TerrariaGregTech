#nullable enable
namespace GregTechCEuTerraria.Api.Machine.Multiblock;

// LOCKED - verbatim port of com.gregtechceu.gtceu.api.machine.multiblock.IBatteryData.
//
// Marker contract for batteries that can slot into a Power Substation's
// battery part - gives the substation the battery's tier, capacity, and
// display name so it can sum capacity across all installed batteries and
// rate-limit charge/discharge by tier.
//
// Implemented by the eventual `BatteryItem` when the substation hatch lands.
public interface IBatteryData
{
	int Tier { get; }

	long Capacity { get; }

	string BatteryName { get; }
}
