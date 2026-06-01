#nullable enable
using GregTechCEuTerraria.Api.Machine.Feature;
using GregTechCEuTerraria.Api.Machine.Trait;

namespace GregTechCEuTerraria.Api.Recipe.Modifier;

// Verbatim port of com.gregtechceu.gtceu.api.recipe.modifier.RecipeModifier
// List. Represents a list of RecipeModifiers applied in order.
//
// Composition semantics (verbatim with upstream):
//   - Walk the modifiers in declared order. Each modifier sees the recipe
//     as transformed by the previous modifiers in the chain (`runningRecipe`).
//   - The composed ModifierFunction returned at the end is built via
//     `func.compose(result)` per step - same call site as upstream.
//   - If any step's func cancels the recipe (`apply` returns null), record
//     the failure reason against the ORIGINAL recipe and return NULL.
//
// Why we ported this even though most call sites use a single modifier:
//   `WorkableElectricMultiblockMachine` (upstream) does `instanceof
//   RecipeModifierList list && Arrays.stream(list.getModifiers()).anyMatch(...)`
//   to inspect its modifier chain. Concrete electric multis depend on this
//   introspection working when wired.
public sealed class RecipeModifierList : RecipeModifier
{
	private readonly RecipeModifier[] _modifiers;

	public RecipeModifier[] Modifiers => _modifiers;

	public RecipeModifierList(params RecipeModifier[] modifiers) : base()
	{
		_modifiers = modifiers;
	}

	// Builds the final ModifierFunction by applying each RecipeModifier in
	// order, tracking the recipe as each modifier is applied.
	public override ModifierFunction GetModifier(IRecipeLogicMachine machine, GTRecipe recipe)
	{
		ModifierFunction result = ModifierFunction.IDENTITY;
		GTRecipe? runningRecipe = recipe;
		foreach (var modifier in _modifiers)
		{
			var func = modifier.GetModifier(machine, runningRecipe!);
			runningRecipe = func.Apply(runningRecipe!);
			if (runningRecipe is null)
			{
				RecipeLogic.PutFailureReason(machine, recipe, func.FailReason);
				return ModifierFunction.NULL;
			}
			result = func.Compose(result);
		}
		return result;
	}
}
