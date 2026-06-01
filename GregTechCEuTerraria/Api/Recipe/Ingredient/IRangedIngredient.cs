#nullable enable
using System;
using GregTechCEuTerraria.Api.Util.ValueProviders;

namespace GregTechCEuTerraria.Api.Recipe.Ingredient;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.recipe.ingredient.IRangedIngredient.
//
// The shared surface of ingredients whose count is a rolled range -
// `IntProviderIngredient` (items) and `IntProviderFluidIngredient` (fluids)
// both implement this so output-display code (`MultiblockDisplayText.
// addOutputLines`, the recipe browser's expected-yield computation) can
// query the average roll uniformly.
//
// Adaptations:
//   - upstream `RandomSource` -> System.Random.
//   - upstream declares `rollSampledCount()` as a `default` method passing
//     `GTValues.RNG`. We have no project-wide RNG (each impl carries its own
//     static `_rng` matching the established Api/Recipe pattern), so the
//     no-arg overload is declared here without a default and each impl
//     provides `RollSampledCount() => RollSampledCount(_rng)`. Functionally
//     equivalent to upstream from the caller's side.
public interface IRangedIngredient
{
	IntProvider GetCountProvider();

	int GetSampledCount();

	void SetSampledCount(int count);

	// Passthrough to RollSampledCount(Random) with each impl's shared `_rng`.
	int RollSampledCount();

	int RollSampledCount(Random random);

	// Average of the count provider's min/max - the expected yield for output-
	// display lines (port-locale tooltips, recipe browser).
	double GetMidRoll() =>
		(GetCountProvider().GetMaxValue() + GetCountProvider().GetMinValue()) / 2.0;

	bool IsRolled() => GetSampledCount() != -1;

	void Reset();
}
