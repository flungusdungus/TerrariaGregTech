#nullable enable
namespace GregTechCEuTerraria.Api.Fluids;

// LOCKED - verbatim port of com.gregtechceu.gtceu.api.fluids.FluidState.
// DO NOT modify behavior; mirror upstream changes only.
//
// Physical state of a fluid for rendering / interaction purposes.
//
// Documented adaptation:
//   - Upstream's `tagKey : TagKey<Fluid>` (Forge cross-mod fluid tag system,
//     e.g. forge:gases) is dropped - Terraria has no cross-mod fluid tag
//     registry, and "which fluids are GAS" is answerable via direct
//     `fluid.State == FluidState.GAS` enum comparison. No information loss
//     for our world model.
//
// Constants per value:
//   - TranslationKey: i18n key for the state display name. Same format as
//     upstream ("gtceu.fluid.state_<lower>"). Looked up via tML's
//     Language.GetOrRegister at first read; fluid-state-bearing UI uses it
//     for tooltips.
//
//   LIQUID - flows downward, normal tank fill animation.
//   GAS    - rises upward in tanks, tinted lighter, doesn't pool when free.
//   PLASMA - emits light, particle FX, high-temperature container required.
//
// FluidState is NOT the same as FluidStorageKey (which is the registration
// slot a material occupies - see FluidStorageKey). The same fluid type can
// be stored under multiple keys with different states; e.g. liquid_iron is
// stored under MOLTEN with state LIQUID.
public enum FluidState
{
	LIQUID,
	GAS,
	PLASMA,
}

public static class FluidStateExtensions
{
	// Verbatim port of upstream's @Getter `translationKey` field. Values match
	// upstream exactly so locale files (or any future cross-tooling that
	// extracts upstream's strings) stay portable.
	public static string TranslationKey(this FluidState state) => state switch
	{
		FluidState.LIQUID => "gtceu.fluid.state_liquid",
		FluidState.GAS    => "gtceu.fluid.state_gas",
		FluidState.PLASMA => "gtceu.fluid.state_plasma",
		_ => throw new System.ArgumentOutOfRangeException(nameof(state)),
	};
}
