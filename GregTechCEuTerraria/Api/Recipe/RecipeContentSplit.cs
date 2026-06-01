#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Recipe.Chance.Logic;
using GregTechCEuTerraria.Api.Recipe.Content;

namespace GregTechCEuTerraria.Api.Recipe;

// Port of com.gregtechceu.gtceu.api.recipe.RecipeRunner.fillContentMatchList
// (RecipeRunner.java:75-119).
//
// Splits a recipe's per-capability `List<Content>` into two parallel lists:
//
//   searchContents   - payloads used during the MATCH pass (simulate=true).
//                      Always includes every entry: tools, chanced, guaranteed.
//                      All must be matchable for the recipe to qualify.
//
//   consumeContents  - payloads used during the CONSUME pass (simulate=false).
//                      Excludes tools (chance==0 && tierChanceBoost==0); they
//                      must be PRESENT but never extracted. Includes
//                      guaranteed (chance>=maxChance) directly. Rolls chanced
//                      (chance>0 || tierChanceBoost>0) through ChanceLogic
//                      and includes survivors.
//
// Upstream behaviour, line for line:
//
//   if (simulated) continue;                                     // line 96
//   if (cont.chance >= cont.maxChance)
//       contentList.add(cont.content);                           // line 99
//   else if (cont.chance > 0 || cont.tierChanceBoost > 0)
//       chancedContents.add(cont);                               // line 101
//   // Do not add Non-Consumed ingredients                       // line 103
//
//   chancedContents = logic.roll(...);                           // line 109
//   for (cont : chancedContents) contentList.add(cont.content);  // line 113
//
// Adapted bits:
//   - `searchContents` is exposed as a plain payload list because our
//     trait HandleRecipeInner takes `List<Ingredient>` / `List<FluidIngredient>`,
//     not a Content list.
//   - `chanceCaches` is the flat Dictionary<string, int> on RecipeLogic
//     (see RecipeLogic comment about ChanceCacheMap collapse); the
//     ChanceLogic.Roll API expects `IDictionary<object, int>` keyed by
//     payload object, so we provide an adapter when wiring.
public static class RecipeContentSplit
{
	// Returns the per-capability MATCH list (everything) and CONSUME list
	// (filtered + rolled) - both as payload `object` lists matching the
	// dispatcher's existing shape.
	//
	// totalRuns defaults to 1 (single recipe per call); parallel-batch
	// callers pass `recipe.GetTotalRuns()`.
	public static (List<object> Match, List<object> Consume) Split(
		object cap,
		IReadOnlyList<Content.Content> entries,
		IO io, bool isTick, GTRecipe recipe,
		IDictionary<object, int>? chanceCache,
		int totalRuns = 1)
	{
		var match   = new List<object>(entries.Count);
		var consume = new List<object>(entries.Count);
		List<Content.Content>? chanced = null;

		foreach (var cont in entries)
		{
			match.Add(cont.Payload);

			if (cont.Chance >= cont.MaxChance)
			{
				consume.Add(cont.Payload);
			}
			else if (cont.Chance > 0 || cont.TierChanceBoost > 0)
			{
				(chanced ??= new List<Content.Content>()).Add(cont);
			}
			// else: tool (chance==0 && tierChanceBoost==0) - match-only.
		}

		if (chanced is not null && chanced.Count > 0)
		{
			var logic = recipe.GetChanceLogicForCapability(cap, io, isTick);
			int recipeTier = RecipeHelper.GetPreOCRecipeEuTier(recipe);
			int chanceTier = recipeTier + recipe.OcLevel;
			var function = recipe.RecipeType.ChanceFunction;
			var rolled = logic.Roll(cap, chanced, function, recipeTier, chanceTier, chanceCache, totalRuns);
			foreach (var c in rolled) consume.Add(c.Payload);
		}

		return (match, consume);
	}
}
