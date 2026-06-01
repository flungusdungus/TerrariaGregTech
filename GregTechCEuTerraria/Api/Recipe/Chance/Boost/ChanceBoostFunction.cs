#nullable enable
using System;
using GregTechCEuTerraria.Common.Energy;
using RecipeContent = GregTechCEuTerraria.Api.Recipe.Content.Content;

namespace GregTechCEuTerraria.Api.Recipe.Chance.Boost;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.recipe.chance.boost.ChanceBoostFunction.
//
// A function that boosts a Content's chance based on the overclock-tier
// difference between the recipe's base tier and the tier it's actually run at.
// Used by `ChanceLogic` (which we have ported) when picking which chanced
// outputs roll, and consumed by output-display code (`MultiblockDisplayText.
// addOutputLines` and the in-machine recipe browser) to show expected yields.
//
// Adaptations:
//   - Java functional interface -> sealed delegate-wrapping class (the project
//     convention used by `ModifierFunction`, see Api/Recipe/Modifier/).
//   - `Mth.clamp` -> `Math.Clamp` (identical semantics).
//   - `GTValues.ULV` -> `(int)VoltageTier.ULV` (= 0).
public sealed class ChanceBoostFunction
{
	private readonly Func<RecipeContent, int, int, int> _apply;

	public ChanceBoostFunction(Func<RecipeContent, int, int, int> apply)
	{
		_apply = apply;
	}

	// Boosts `entry.Chance` based on the (chanceTier - recipeTier) difference.
	public int GetBoostedChance(RecipeContent entry, int recipeTier, int chanceTier) =>
		_apply(entry, recipeTier, chanceTier);

	// Chance boosting function based on the number of performed overclocks.
	public static readonly ChanceBoostFunction OVERCLOCK = new((entry, recipeTier, chanceTier) =>
	{
		int tierDiff = chanceTier - recipeTier;
		if (tierDiff <= 0) return entry.Chance; // equal or invalid tiers do not boost at all
		if (recipeTier == (int)VoltageTier.ULV) tierDiff--; // LV does not boost over ULV
		return Math.Clamp(entry.Chance + (entry.TierChanceBoost * tierDiff), 0, entry.MaxChance);
	});

	// No-op boosting function - returns Content.Chance unchanged.
	public static readonly ChanceBoostFunction NONE = new((entry, _, _) => entry.Chance);
}
