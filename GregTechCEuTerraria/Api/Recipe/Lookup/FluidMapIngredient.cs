#nullable enable
namespace GregTechCEuTerraria.Api.Recipe.Lookup;

// ADAPTED - stands in for upstream's
// com.gregtechceu.gtceu.api.recipe.lookup.ingredient.fluid.FluidStackMapIngredient
// + FluidTagMapIngredient.
//
// Trie node-key for one concrete fluid, keyed on FluidType.Id.
//
// Documented adaptation: same as ItemMapIngredient - upstream keeps a separate
// fluid-tag key; our FluidIngredient pre-resolves to a flat FluidType list
// (FluidIngredient.GetFluids(), which also flattens attribute matches like
// ACID), so a recipe's fluid ingredient is expanded at compile time into one
// FluidMapIngredient per resolved fluid.
public sealed class FluidMapIngredient : AbstractMapIngredient
{
	// FluidType.Id - the fluid identity string.
	public readonly string FluidId;

	public FluidMapIngredient(string fluidId)
	{
		FluidId = fluidId;
	}

	protected override int Hash() => FluidId.GetHashCode();

	protected override bool EqualsSameClass(AbstractMapIngredient other) =>
		// EqualsSameClass is only called when `other` is the same class.
		FluidId == ((FluidMapIngredient)other).FluidId;

	public override string ToString() => $"FluidMapIngredient{{id={FluidId}}}";
}
