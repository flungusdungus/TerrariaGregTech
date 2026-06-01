#nullable enable
namespace GregTechCEuTerraria.Api.Capability.Recipe;

// LOCKED - verbatim port of com.gregtechceu.gtceu.api.capability.recipe.IO.
// DO NOT modify behavior; only mirror upstream changes.
//
// Direction of capability flow. Used by:
//   - GTRecipe content access: GetContents(IO io) returns Inputs / Outputs.
//   - NotifiableEnergyContainer / NotifiableFluidTank /
//     NotifiableItemStackHandler (pending port) - each trait carries its role
//     as a field set at construction (emitter = OUT, receiver = IN,
//     bidirectional buffer = BOTH).
//   - IFilteredHandler.GetFilteringRoles (pending port) - covers declare which
//     direction they filter on.
//   - RecipeLogic.handleRecipeIO (already implicit in our
//     WorkableTieredMachine.TryConsumeInputs / output-deposit split;
//     the verbatim port will consolidate into one method that takes IO).
//
//   IN   - capability acts as input (consumer / sink).
//   OUT  - capability acts as output (producer / source).
//   BOTH - bidirectional (battery buffer trait, transformer).
//   NONE - disabled.
//
// Ordinal values match upstream enum declaration order so NBT-saved values
// remain compatible if upstream's ordinal usage is ever ported.
public enum IO : byte
{
	IN   = 0,
	OUT  = 1,
	BOTH = 2,
	NONE = 3,
}

public static class IOExtensions
{
	// Verbatim port of upstream IO.support(IO io). Asks "this" handler:
	// do you support traffic of direction `io`?
	//   - identical direction       -> true
	//   - other is NONE             -> false
	//   - this is BOTH (non-NONE)   -> true
	//   - otherwise                 -> false
	public static bool Supports(this IO self, IO io)
	{
		if (io == self) return true;
		if (io == IO.NONE) return false;
		return self == IO.BOTH;
	}

	// Verbatim port of upstream's per-IO `tooltip` field - i18n key for the
	// direction label shown in IO-mode selectors and cover tooltips. Values
	// match upstream exactly so locale files stay portable.
	public static string Tooltip(this IO self) => self switch
	{
		IO.IN   => "gtceu.io.import",
		IO.OUT  => "gtceu.io.export",
		IO.BOTH => "gtceu.io.both",
		IO.NONE => "gtceu.io.none",
		_ => throw new System.ArgumentOutOfRangeException(nameof(self)),
	};
}
