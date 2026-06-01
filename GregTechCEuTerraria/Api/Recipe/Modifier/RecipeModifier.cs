#nullable enable
using System;
using GregTechCEuTerraria.Api.Machine.Feature;

namespace GregTechCEuTerraria.Api.Recipe.Modifier;

// Port of com.gregtechceu.gtceu.api.recipe.modifier.RecipeModifier.
//
// A function that returns a ModifierFunction given a machine and a GTRecipe.
// Upstream is a @FunctionalInterface; we expose two shapes:
//   - delegate-wrapping via `new RecipeModifier(fn)` for the common single-
//     function case (the bulk of GTRecipeModifiers).
//   - subclass override of `GetModifier` for richer modifiers that carry
//     state or need to participate in the multiblock introspection pattern
//     (`RecipeModifierList`, `EfficiencyModifier`, ...). Matches upstream's
//     `class X implements RecipeModifier` idiom where callers do
//     `getRecipeModifier() instanceof RecipeModifierList`.
//
// Documented adaptation:
//   - Upstream's functional parameter is MetaMachine. Our MetaMachine is
//     mod-side glue (TerrariaCompat/), and the Api/ layer must not depend on
//     it - so, like the rest of the Api recipe surface, the machine is typed
//     as IRecipeLogicMachine. Modifiers downcast to IOverclockMachine etc.
public class RecipeModifier
{
	private readonly Func<IRecipeLogicMachine, GTRecipe, ModifierFunction>? _fn;

	// Delegate-wrapping ctor - the common shape for GTRecipeModifiers entries.
	public RecipeModifier(Func<IRecipeLogicMachine, GTRecipe, ModifierFunction> fn) => _fn = fn;

	// Subclass ctor - callers must override GetModifier.
	protected RecipeModifier() => _fn = null;

	// Identity modifier.
	public static readonly RecipeModifier NO_MODIFIER = new((_, _) => ModifierFunction.IDENTITY);

	// Get the ModifierFunction for the given machine + recipe state.
	// Subclasses override; the delegate-ctor path uses the captured fn.
	public virtual ModifierFunction GetModifier(IRecipeLogicMachine machine, GTRecipe recipe) =>
		_fn is null
			? throw new InvalidOperationException(
				$"{GetType().Name} did not override GetModifier and was not constructed with a delegate.")
			: _fn(machine, recipe);

	// Get the ModifierFunction and immediately apply it. Returns the modified
	// recipe, or null if the modifier cancels it.
	public GTRecipe? ApplyModifier(IRecipeLogicMachine machine, GTRecipe recipe) =>
		GetModifier(machine, recipe).Apply(recipe);

	// Port of RecipeModifier.nullWrongType. Upstream logs the misuse via
	// GTCEu.LOGGER; the Api/ layer has no logger, so the log is dropped
	// (callers in TerrariaCompat may log themselves).
	public static ModifierFunction NullWrongType() => ModifierFunction.NULL;
}
