#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Recipes;

// Reads Data/Recipes/all.json (the runData snapshot produced by
// snapshot-recipes.py) and materializes GTRecipe instances into RecipeRegistry.
// Called from Mod.Load after MaterialRegistry + FluidRegistry + resolver ready.
// Bundle = JSON array; per-entry shape matches GTRecipeSerializer.
public static class RecipeJsonLoader
{
	private const string BundlePath = "Data/Recipes/all.json";

	public static void Load(Mod mod, IIngredientResolver resolver)
	{
		using var stream = mod.GetFileStream(BundlePath);
		if (stream is null)
		{
			mod.Logger.Warn($"Recipe bundle not found at {BundlePath} - no recipes loaded. " +
			                 "Run `./gradlew runData` upstream + " +
			                 "`python tools/scripts/snapshot-recipes.py` to produce it.");
			return;
		}

		using var doc = JsonDocument.Parse(stream);
		if (doc.RootElement.ValueKind != JsonValueKind.Array)
		{
			mod.Logger.Error($"Recipe bundle is not a JSON array (got {doc.RootElement.ValueKind})");
			return;
		}

		var byStation = new Dictionary<string, List<GTRecipe>>();
		int total = 0, skipped = 0;

		foreach (var el in doc.RootElement.EnumerateArray())
		{
			string id = el.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
				? (idEl.GetString() ?? "")
				: "";
			if (string.IsNullOrEmpty(id)) { skipped++; continue; }

			// Non-destructive bundle override - tuned variant in CompatRecipes.Build.
			if (CompatRecipes.OverriddenIds.Contains(id)) { skipped++; continue; }

			GTRecipe recipe;
			try
			{
				// Vanilla MC recipes use the native shape (-> VanillaRecipeJson);
				// GTCEu uses the capability-map shape (-> GTRecipeSerializer).
				recipe = VanillaRecipeJson.IsVanillaShape(el)
					? VanillaRecipeJson.Read(el, resolver, id)
					: GTRecipeSerializer.Read(el, resolver, id);
			}
			catch (System.Exception ex)
			{
				mod.Logger.Warn($"Skipping recipe {id}: {ex.Message}");
				skipped++;
				continue;
			}

			string station = recipe.RecipeType.RegistryName;
			if (!byStation.TryGetValue(station, out var list))
			{
				list = new List<GTRecipe>();
				byStation[station] = list;
			}
			list.Add(recipe);
			total++;

			// Verbatim GTRecipeSerializer.fromJson:149-155 - a recipe with a
			// ResearchCondition registers research_id -> recipe in the type's
			// data-stick map (DataAccessHatch uses it to unlock recipes).
			foreach (var cond in recipe.Conditions)
			{
				if (cond is Common.Recipe.Condition.ResearchCondition rc && !string.IsNullOrEmpty(rc.ResearchId))
					recipe.RecipeType.AddDataStickEntry(rc.ResearchId, recipe);
			}
		}

		// Supplemental compat recipes parsed through the same serializer.
		foreach (var (station, recipe) in CompatRecipes.Build(resolver))
		{
			if (!byStation.TryGetValue(station, out var compatList))
			{
				compatList = new List<GTRecipe>();
				byStation[station] = compatList;
			}
			compatList.Add(recipe);
			total++;
		}

		// Macerator shortcuts for vanilla Terraria ores: 1 ore = 16 raw_X (per
		// the workbench hand recipe in VanillaCraftingBridgeSystem), so the
		// macerator accepts vanilla ore directly at 16x output, 2x EU/t. Runs
		// after the bundle pass to clone from the source raw_X recipes.
		foreach (var derived in CompatRecipes.BuildVanillaOreMaceratorRecipes(byStation))
		{
			if (!byStation.TryGetValue("macerator", out var maceratorList))
			{
				maceratorList = new List<GTRecipe>();
				byStation["macerator"] = maceratorList;
			}
			maceratorList.Add(derived);
			total++;
		}

		// Pass A: smelting -> electric_furnace ULV proxy (see NativeRecipeProxy).
		NativeRecipeProxy.SynthesizeFromSmelting(byStation);

		var map = new Dictionary<string, IReadOnlyList<GTRecipe>>(byStation.Count);
		foreach (var (station, list) in byStation) map[station] = list;
		RecipeRegistry.Set(map);

		mod.Logger.Info($"Loaded {total:N0} recipes across {byStation.Count} stations" +
		                 (skipped > 0 ? $" (skipped {skipped})" : "") + ".");
	}
}
