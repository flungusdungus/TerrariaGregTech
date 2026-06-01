#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Category;
using GregTechCEuTerraria.Api.Recipe.Chance.Logic;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using Terraria.ModLoader.IO;
using RecipeContent = GregTechCEuTerraria.Api.Recipe.Content.Content;

namespace GregTechCEuTerraria.TerrariaCompat.Recipes;

// Parses VANILLA MC recipe JSON into a GTRecipe.
//
// runData dumps GTCEu machine recipes in the capability-map shape but vanilla
// crafting/smelting in MC native shape (key/pattern/result / ingredient/result).
// GTRecipeSerializer only reads the former - without this bridge, all ~10.7k
// vanilla recipes silently dropped. Handles crafting_shaped(_strict),
// crafting_shapeless, smelting, blasting, smoking, campfire_cooking.
public static class VanillaRecipeJson
{
	// Vanilla = `result`; GTCEu = `outputs`. Clean discriminator.
	public static bool IsVanillaShape(JsonElement el) =>
		el.TryGetProperty("result", out _) && !el.TryGetProperty("outputs", out _);

	public static GTRecipe Read(JsonElement root, IIngredientResolver resolver, string id)
	{
		string type = root.TryGetProperty("type", out var t) ? (t.GetString() ?? "") : "";
		string station = type;
		int colon = station.IndexOf(':');
		if (colon >= 0) station = station[(colon + 1)..];
		var recipeType = GTRecipeType.GetOrCreate(station);

		var inputs = new List<RecipeContent>();
		// 2x2-fitting content = hand-craftable; bridge registers it with no tile.
		bool handCraftable = false;

		// Shaped - count each pattern symbol; bounding box -> handCraftable.
		if (root.TryGetProperty("pattern", out var pattern) && pattern.ValueKind == JsonValueKind.Array
		    && root.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.Object)
		{
			var counts = new Dictionary<char, int>();
			int minR = int.MaxValue, maxR = -1, minC = int.MaxValue, maxC = -1, r = 0;
			foreach (var row in pattern.EnumerateArray())
			{
				string s = row.GetString() ?? "";
				for (int c = 0; c < s.Length; c++)
				{
					if (s[c] == ' ') continue;
					counts[s[c]] = counts.GetValueOrDefault(s[c]) + 1;
					if (r < minR) minR = r;
					if (r > maxR) maxR = r;
					if (c < minC) minC = c;
					if (c > maxC) maxC = c;
				}
				r++;
			}
			handCraftable = maxR >= 0 && maxR - minR <= 1 && maxC - minC <= 1;
			foreach (var (sym, count) in counts)
				if (key.TryGetProperty(sym.ToString(), out var ingEl))
					inputs.Add(ItemContent(ParseIngredient(ingEl, resolver), count));
		}
		// Shapeless - merge identical entries by count.
		else if (root.TryGetProperty("ingredients", out var ings) && ings.ValueKind == JsonValueKind.Array)
		{
			handCraftable = ings.GetArrayLength() <= 4;
			var grouped = new Dictionary<string, (JsonElement el, int n)>();
			foreach (var ig in ings.EnumerateArray())
			{
				string k = ig.GetRawText();
				grouped[k] = grouped.TryGetValue(k, out var g) ? (g.el, g.n + 1) : (ig, 1);
			}
			foreach (var (_, (el, n)) in grouped)
				inputs.Add(ItemContent(ParseIngredient(el, resolver), n));
		}
		// Cooking - single ingredient.
		else if (root.TryGetProperty("ingredient", out var single))
		{
			inputs.Add(ItemContent(ParseIngredient(single, resolver), 1));
		}

		var outputs = new List<RecipeContent>();
		if (root.TryGetProperty("result", out var result))
		{
			int outCount = result.ValueKind == JsonValueKind.Object && result.TryGetProperty("count", out var cEl)
				? cEl.GetInt32() : 1;
			outputs.Add(ItemContent(ParseIngredient(result, resolver), outCount < 1 ? 1 : outCount));
		}

		var inMap  = new Dictionary<object, List<RecipeContent>>();
		var outMap = new Dictionary<object, List<RecipeContent>>();
		if (inputs.Count  > 0) inMap[ItemRecipeCapability.CAP]  = inputs;
		if (outputs.Count > 0) outMap[ItemRecipeCapability.CAP] = outputs;

		var data = new TagCompound();
		// Hand flag -> VanillaCraftingBridge registers with no tile.
		if (handCraftable) data["GT.Hand"] = true;

		return new GTRecipe(
			recipeType:              recipeType,
			id:                      id,
			inputs:                  inMap,
			outputs:                 outMap,
			tickInputs:              new Dictionary<object, List<RecipeContent>>(),
			tickOutputs:             new Dictionary<object, List<RecipeContent>>(),
			inputChanceLogics:       new Dictionary<object, ChanceLogic>(),
			outputChanceLogics:      new Dictionary<object, ChanceLogic>(),
			tickInputChanceLogics:   new Dictionary<object, ChanceLogic>(),
			tickOutputChanceLogics:  new Dictionary<object, ChanceLogic>(),
			conditions:              new List<RecipeCondition>(),
			ingredientActions:       System.Array.Empty<object>(),
			data:                    data,
			duration:                0,
			recipeCategory:          GTRecipeCategory.DEFAULT,
			groupColor:              -1);
	}

	private static RecipeContent ItemContent(Ingredient ing, int count)
	{
		int max = ChanceLogic.GetMaxChancedValue();
		Ingredient payload = count > 1 ? SizedIngredient.Create(ing, count) : ing;
		return new RecipeContent(payload, max, max, 0);
	}

	// {"item":...} | {"tag":...} | bare string | [opt,...] (first option wins).
	// Unresolvable refs return empty ItemStackIngredient - bridge rejects.
	private static Ingredient ParseIngredient(JsonElement el, IIngredientResolver resolver)
	{
		switch (el.ValueKind)
		{
			case JsonValueKind.String:
				string itemId = el.GetString() ?? "";
				return new ItemStackIngredient(resolver.ResolveItemType(itemId), itemId);
			case JsonValueKind.Array:
				foreach (var opt in el.EnumerateArray())
					return ParseIngredient(opt, resolver);   // first option
				return new ItemStackIngredient(0, "");
			default:
				try { return IngredientJson.Read(el, resolver); }
				catch { return new ItemStackIngredient(0, ""); }
		}
	}
}
