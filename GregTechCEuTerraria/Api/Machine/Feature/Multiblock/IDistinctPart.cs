#nullable enable
namespace GregTechCEuTerraria.Api.Machine.Feature.Multiblock;

// Port of com.gregtechceu.gtceu.api.machine.feature.multiblock.IDistinctPart.
//
// Marker for an input bus that opts into per-bus distinctness - the
// controller routes recipes per-bus instead of pooling all input contents.
// Used by the assembler / EBF families to allow simultaneous different
// recipes through different input buses.
//
// Documented adaptations:
//   - `attachConfigurators(ConfiguratorPanel)` / `superAttachConfigurators`
//     DROPPED - these wire a fancy-toggle button into the LDLib
//     `ConfiguratorPanel`. We have no equivalent panel; UI for toggling
//     distinctness comes via the existing per-part settings popup once a
//     concrete distinct-bus part lands. Net surface here is just the
//     state-bearing pair.
public interface IDistinctPart : IMultiPart
{
	bool IsDistinct();
	void SetDistinct(bool isDistinct);
}
