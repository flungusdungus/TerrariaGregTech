#nullable enable
using System;
using GregTechCEuTerraria.Api.Capability.Recipe;

namespace GregTechCEuTerraria.Api.Machine.Feature;

// Port of com.gregtechceu.gtceu.api.machine.feature.IVoidable.
//
// A machine that can void selected recipe outputs (items, fluids, or both)
// based on its `MultiblockVoidingMode`. Read by `RecipeRunner` /
// `WorkableMultiblockMachine`'s recipe-execution hooks to skip output
// deposit for voided capabilities.
//
// === Documented adaptations =================================================
//
//   - `IMachineFeature` parent DROPPED - same reason as other multi feature
//     interfaces; consumers cast `this` to `MetaMachine` directly.
//   - `attachConfigurators` UI helper DROPPED - no LowDragLib configurator
//     panel; the voiding-mode UI selector lands separately when the multi
//     GUI does.
//   - `getOutputLimits()` (per-capability output cap on the
//     MachineDefinition) DROPPED - not on our `MachineDefinition` yet; add
//     when a multi needs per-cap output limits.
//   - `RecipeCapability<?>` parameter -> `object` (our capability tokens are
//     non-generic singletons like `ItemRecipeCapability.CAP`).
public interface IVoidable
{
	// Does this machine void recipe outputs for the given capability? Used
	// by the recipe-execution dispatcher to skip output deposit / clear
	// remaining contents for voided caps.
	bool CanVoidRecipeOutputs(object capability) =>
		GetVoidingMode().CanVoid(capability);

	void SetVoidingMode(MultiblockVoidingMode mode) { }

	MultiblockVoidingMode GetVoidingMode() => MultiblockVoidingMode.VoidNone;
}

// LOCKED - verbatim port of the upstream `IVoidable.VoidingMode` enum.
// Four-state: none, items, fluids, both. Each value carries a `CanVoid(cap)`
// predicate matching upstream's lambda.
public enum MultiblockVoidingMode
{
	VoidNone        = 0,
	VoidItems       = 1,
	VoidFluids      = 2,
	VoidItemsFluids = 3,
}

public static class MultiblockVoidingModeExtensions
{
	public static bool CanVoid(this MultiblockVoidingMode mode, object capability) => mode switch
	{
		MultiblockVoidingMode.VoidNone        => false,
		MultiblockVoidingMode.VoidItems       => capability == ItemRecipeCapability.CAP,
		MultiblockVoidingMode.VoidFluids      => capability == FluidRecipeCapability.CAP,
		MultiblockVoidingMode.VoidItemsFluids => capability == ItemRecipeCapability.CAP
		                                       || capability == FluidRecipeCapability.CAP,
		_ => false,
	};

	// Localisation key matching upstream's `localeName` strings - useful when
	// the voiding-mode selector UI is wired.
	public static string LocaleName(this MultiblockVoidingMode mode) => mode switch
	{
		MultiblockVoidingMode.VoidNone        => "gtceu.gui.no_voiding",
		MultiblockVoidingMode.VoidItems       => "gtceu.gui.item_voiding",
		MultiblockVoidingMode.VoidFluids      => "gtceu.gui.fluid_voiding",
		MultiblockVoidingMode.VoidItemsFluids => "gtceu.gui.all_voiding",
		_ => "gtceu.gui.no_voiding",
	};
}
