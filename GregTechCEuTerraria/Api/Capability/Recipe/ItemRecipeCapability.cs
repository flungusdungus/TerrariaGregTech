#nullable enable
using GregTechCEuTerraria.Api.Recipe.Content;
using GregTechCEuTerraria.Api.Recipe.Ingredient;

namespace GregTechCEuTerraria.Api.Capability.Recipe;

// STUB - port of
// com.gregtechceu.gtceu.api.capability.recipe.ItemRecipeCapability.
//
// Identity token for item ingredients in recipes. RecipeLogic groups all
// item inputs/outputs under this capability and dispatches to whichever
// handler returns this from `getCapability()` - typically the machine's
// NotifiableItemStackHandler trait.
//
// Documented deferrals (lands with full RecipeLogic port):
//   - Recipe-side helpers (.of, .makeContent, .createContent serialization,
//     UI widget creation) dropped - those land with the JEI-style recipe
//     browser polish.
//
// Payload type on Content carrying an item ingredient is Ingredient (our
// Forge-Ingredient analogue - abstract base with ItemStackIngredient /
// TagIngredient / SizedIngredient / IntCircuitIngredient / IntProvider /
// NBTPredicate concrete subclasses).
public sealed class ItemRecipeCapability : RecipeCapability<Ingredient>
{
	public static readonly ItemRecipeCapability CAP = new();

	private ItemRecipeCapability() : base("item") { }

	// Ingredient instances are immutable after JSON parse (matchers carry
	// resolved item-type arrays); copy = identity. Upstream's Ingredient.copy
	// matches this - it's a wrapper around the same value list.
	public override Ingredient CopyInner(Ingredient content) => content;

	// Verbatim port of upstream `ItemRecipeCapability.copyWithModifier`
	// (ItemRecipeCapability.java:69-78). Critical for parallel / overclock
	// modifiers - without this, parallels=N modifiers silently leave inputs at
	// their original count.
	//
	// Upstream's `IntProviderIngredient` branch wraps the inner provider via
	// `ModifiedIntProvider.of(provider, modifier)`. We don't have that wrapper
	// today; for now the IntProvider case collapses to a fixed SizedIngredient
	// at `modifier.apply(meanCount)` so the modifier doesn't silently no-op.
	// This is a documented partial port - no recipe in the bundled set uses
	// IntProvider amounts with parallel scaling.
	public override Ingredient CopyWithModifier(Ingredient content, ContentModifier modifier)
	{
		if (content is SizedIngredient sized)
			return SizedIngredient.Create(sized.Inner, modifier.Apply(sized.Amount));
		if (content is IntProviderIngredient provider)
		{
			// Partial: collapses random count to scaled mean.
			int mean = (provider.CountProvider.GetMinValue() + provider.CountProvider.GetMaxValue()) / 2;
			return SizedIngredient.Create(provider.Inner, modifier.Apply(System.Math.Max(1, mean)));
		}
		// Bare Ingredient (no count carrier) - upstream wraps with SizedIngredient
		// at modifier.apply(1).
		return SizedIngredient.Create(content, modifier.Apply(1));
	}
}
