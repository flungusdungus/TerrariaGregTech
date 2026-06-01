#nullable enable
using GregTechCEuTerraria.Api.Recipe.Content;
using GregTechCEuTerraria.Api.Recipe.Ingredient;

namespace GregTechCEuTerraria.Api.Capability.Recipe;

// STUB - port of
// com.gregtechceu.gtceu.api.capability.recipe.FluidRecipeCapability.
//
// Identity token for fluid ingredients in recipes. RecipeLogic groups all
// fluid inputs/outputs under this capability and dispatches to the
// machine's NotifiableFluidTank trait.
//
// Documented deferrals (same shape as ItemRecipeCapability):
//   - Recipe-side .of / .makeContent / serialization / UI helpers deferred.
//
// Upstream-verbatim: payload type on Content carrying a fluid input/output
// is FluidIngredient. IngredientJson dispatches fluid forms ({"fluid":...},
// {"tag":...}, {"attribute":...}) to construct a FluidIngredient which is
// then stored as the Content.Payload. NotifiableFluidTank's handler walker
// reads it back via `(FluidIngredient)content.Payload`.
public sealed class FluidRecipeCapability : RecipeCapability<FluidIngredient>
{
	public static readonly FluidRecipeCapability CAP = new();

	private FluidRecipeCapability() : base("fluid") { }

	public override FluidIngredient CopyInner(FluidIngredient content) => content;

	// Verbatim port of upstream `FluidRecipeCapability.copyWithModifier`
	// (FluidRecipeCapability.java:64-73). Critical for parallel / overclock
	// modifiers - without this, parallels=N modifiers silently leave fluid
	// inputs (e.g. plasma turbine fuel) at their original mB amount.
	//
	// Upstream's `IntProviderFluidIngredient` branch wraps the inner provider
	// via `ModifiedIntProvider.of(...)`. We don't have that wrapper today; for
	// now collapse to a fixed FluidIngredient at `modifier.apply(meanCount)`
	// so the modifier doesn't silently no-op. Documented partial port - no
	// bundled recipe uses IntProvider fluid amounts with parallel scaling.
	public override FluidIngredient CopyWithModifier(FluidIngredient content, ContentModifier modifier)
	{
		if (content.IsEmpty) return CopyInner(content);
		if (content is IntProviderFluidIngredient provider)
		{
			int mean = (provider.CountProvider.GetMinValue() + provider.CountProvider.GetMaxValue()) / 2;
			return BuildCopyWithAmount(provider.Inner, modifier.Apply(System.Math.Max(1, mean)));
		}
		return BuildCopyWithAmount(content, modifier.Apply(content.Amount));
	}

	// Re-constructs a FluidIngredient with the same matching set but a new
	// amount. Upstream's path is `copy.setAmount(...)`; we re-construct because
	// our FluidIngredient pre-resolves its matching list at ctor time and we
	// want a fresh instance (cleaner - avoids mutating shared content payloads).
	private static FluidIngredient BuildCopyWithAmount(FluidIngredient src, int newAmount)
	{
		if (src.ExactType is not null)
			return new FluidIngredient(src.ExactType, newAmount);
		if (src.TagName is not null)
			return new FluidIngredient(src.TagName, src.GetFluids(), newAmount);
		if (src.Attribute is not null)
			return new FluidIngredient(src.Attribute, src.GetFluids(), newAmount);
		// Empty / unresolved - return as-is.
		return src;
	}
}
