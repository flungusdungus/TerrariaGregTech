#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using RecipeContent = GregTechCEuTerraria.Api.Recipe.Content.Content;
using System.Collections.Generic;
using System.Text;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Recipes;

// JEI-style AND-substring recipe search. Query splits into tokens; recipe
// matches iff EVERY token is a substring of its searchable text (recipe id +
// ingredient identifier tokens + resolved display names, lowercased).
// Per-recipe text is cached; recipes are immutable after load.
public static class RecipeSearch
{
	private static readonly Dictionary<GTRecipe, string> _textCache = new();
	private static readonly Dictionary<GTRecipe, string> _outputTextCache = new();

	public static void ClearCache() { _textCache.Clear(); _outputTextCache.Clear(); }

	// Pre-warm at world load so the first keystroke doesn't stutter (lazy
	// TextFor does Item.SetDefaults per ingredient x ~33k recipes).
	public static void WarmCache()
	{
		foreach (var list in RecipeRegistry.ByStation.Values)
			for (int i = 0; i < list.Count; i++)
			{
				TextFor(list[i]);
				OutputTextFor(list[i]);
			}
	}

	// Output-only match - drives the browser's stable sort (producers above
	// consumers). `@`-tokens ignored here (enforced by Matches).
	public static bool MatchesOutputs(GTRecipe recipe, string[] tokens)
	{
		if (tokens.Length == 0) return true;
		string text = OutputTextFor(recipe);
		foreach (string token in tokens)
		{
			if (token.Length == 0 || token[0] == '@') continue;
			if (!text.Contains(token)) return false;
		}
		return true;
	}

	public static bool Matches(GTRecipe recipe, string[] tokens)
	{
		if (tokens.Length == 0) return true;
		string text = TextFor(recipe);
		string station = recipe.RecipeType.RegistryName.ToLowerInvariant();
		foreach (string token in tokens)
		{
			if (token.Length == 0) continue;

			// `@station` substring filter on station id.
			if (token[0] == '@')
			{
				string needle = token.Substring(1);
				if (needle.Length == 0) continue;
				// `@null` - undocumented diagnostic for unresolved-ingredient
				// gaps in our substitution/prefix coverage.
				if (needle == "null")
				{
					if (!HasUnresolvedIngredient(recipe)) return false;
					continue;
				}
				if (!station.Contains(needle, System.StringComparison.OrdinalIgnoreCase)) return false;
				continue;
			}

			if (!text.Contains(token)) return false;
		}
		return true;
	}

	public static string[] Tokenize(string query)
	{
		if (string.IsNullOrWhiteSpace(query)) return System.Array.Empty<string>();
		var parts = query.ToLowerInvariant().Split(' ', '\t');
		var clean = new List<string>(parts.Length);
		foreach (var p in parts) if (p.Length > 0) clean.Add(p);
		return clean.ToArray();
	}

	private static string TextFor(GTRecipe recipe)
	{
		if (_textCache.TryGetValue(recipe, out var cached)) return cached;

		var sb = new StringBuilder();
		sb.Append(IdWithoutStation(recipe)).Append(' ');
		AppendContents(sb, recipe.GetInputContents(ItemRecipeCapability.CAP),  isFluid: false);
		AppendContents(sb, recipe.GetOutputContents(ItemRecipeCapability.CAP), isFluid: false);
		AppendContents(sb, recipe.GetInputContents(FluidRecipeCapability.CAP),  isFluid: true);
		AppendContents(sb, recipe.GetOutputContents(FluidRecipeCapability.CAP), isFluid: true);

		string text = sb.ToString();
		_textCache[recipe] = text;
		return text;
	}

	private static string OutputTextFor(GTRecipe recipe)
	{
		if (_outputTextCache.TryGetValue(recipe, out var cached)) return cached;

		// Deliberately NOT including the recipe id here. The id's recipe-name
		// part (e.g. `macerate_iron_ingot` left after stripping the station
		// prefix) frequently mentions the station's subject, which leaks
		// "macerator" into the output text of every macerator-station recipe
		// even when the actual outputs are iron dust / etc. That defeats the
		// outputs-first sort (TextFor still indexes the id, so general search
		// finds these recipes - they just rank below the ones whose real
		// OUTPUT is a macerator).
		var sb = new StringBuilder();
		AppendContents(sb, recipe.GetOutputContents(ItemRecipeCapability.CAP), isFluid: false);
		AppendContents(sb, recipe.GetOutputContents(FluidRecipeCapability.CAP), isFluid: true);

		string text = sb.ToString();
		_outputTextCache[recipe] = text;
		return text;
	}

	// Recipe.Id is `<station>/<name>` for almost every loaded recipe - bare
	// substring search over the full id would let "macerator" match every
	// recipe whose station prefix happens to be `macerator/`, defeating the
	// `@station` filter's purpose. Strip the leading station segment so only
	// the recipe-specific name part is indexed; the station is reachable
	// EXCLUSIVELY via the `@` token (handled in Matches).
	private static string IdWithoutStation(GTRecipe recipe)
	{
		string id = recipe.Id ?? string.Empty;
		string stationPrefix = recipe.RecipeType.RegistryName + "/";
		if (id.StartsWith(stationPrefix, System.StringComparison.OrdinalIgnoreCase))
			id = id.Substring(stationPrefix.Length);
		return id.ToLowerInvariant();
	}

	private static void AppendContents(StringBuilder sb, IReadOnlyList<RecipeContent> contents, bool isFluid)
	{
		foreach (var content in contents)
			AppendIngredient(sb, (Ingredient)content.Payload, isFluid);
	}

	private static void AppendIngredient(StringBuilder sb, Ingredient ing, bool isFluid)
	{
		switch (ing)
		{
			case ItemStackIngredient isi:
				if (!string.IsNullOrEmpty(isi.UpstreamId))
					sb.Append(StripNamespace(isi.UpstreamId)).Append(' ');
				AppendItemDisplayName(sb, isi.ItemType);
				break;

			case TagIngredient tag:
				sb.Append(StripNamespace(tag.TagName)).Append(' ');
				foreach (var t in tag.GetItems())
					AppendItemDisplayName(sb, t.type);
				break;

			case SizedIngredient sized:
				AppendIngredient(sb, sized.Inner, isFluid);
				break;

			case IntProviderIngredient ipi:
				AppendIngredient(sb, ipi.Inner, isFluid);
				break;

			// IntCircuitIngredient (the programmed-circuit recipe selector) is
			// deliberately NOT indexed: it appears in a huge fraction of
			// machine recipes, so including "circuit" here made the token
			// match almost everything. Real circuit ITEMS still match via
			// their item name / id.
			case IntCircuitIngredient:
				break;

			case NBTPredicateIngredient nbt:
				if (!string.IsNullOrEmpty(nbt.UpstreamId))
					sb.Append(StripNamespace(nbt.UpstreamId)).Append(' ');
				AppendItemDisplayName(sb, nbt.ItemType);
				break;

			case IntProviderFluidIngredient ipfi:
				AppendIngredient(sb, ipfi.Inner, isFluid: true);
				break;

			case FluidIngredient fi:
				if (fi.ExactType is not null)
				{
					sb.Append(fi.ExactType.Id).Append(' ');
					sb.Append(fi.ExactType.DisplayName.ToLowerInvariant()).Append(' ');
				}
				if (fi.TagName is not null)
					sb.Append(StripNamespace(fi.TagName)).Append(' ');
				if (fi.Attribute is not null)
					sb.Append(fi.Attribute.Id).Append(' ');
				foreach (var t in fi.GetFluids())
					sb.Append(t.Id).Append(' ').Append(t.DisplayName.ToLowerInvariant()).Append(' ');
				break;
		}
	}

	private static void AppendItemDisplayName(StringBuilder sb, int itemType)
	{
		if (itemType <= 0) return;
		var probe = new Item();
		probe.SetDefaults(itemType);
		if (!string.IsNullOrEmpty(probe.Name))
			sb.Append(probe.Name.ToLowerInvariant()).Append(' ');
	}

	// Undocumented @null support - walks every ingredient and returns true if
	// any one fails to resolve. Cheap (only iterates when the user explicitly
	// types @null).
	private static bool HasUnresolvedIngredient(GTRecipe recipe)
	{
		foreach (var c in recipe.GetInputContents(ItemRecipeCapability.CAP))
			if (!IsResolvable((Ingredient)c.Payload)) return true;
		foreach (var c in recipe.GetOutputContents(ItemRecipeCapability.CAP))
			if (!IsResolvable((Ingredient)c.Payload)) return true;
		foreach (var c in recipe.GetInputContents(FluidRecipeCapability.CAP))
			if (!IsResolvable((Ingredient)c.Payload)) return true;
		foreach (var c in recipe.GetOutputContents(FluidRecipeCapability.CAP))
			if (!IsResolvable((Ingredient)c.Payload)) return true;
		return false;
	}

	// Mirrors upstream's `!ingredient.isEmpty()`. Each Ingredient overrides
	// IsEmpty correctly (ItemStack/NBT -> ItemType == 0, Tag -> no members,
	// Fluid -> no fluids, the wrappers delegate to Inner). Deliberately NOT
	// GetItems().Count - ItemStackIngredient.GetItems() always returns a
	// 1-element example list (an air item when ItemType == 0), so an
	// unresolved / unported item id would wrongly count as resolvable and
	// `@null` would miss its recipe.
	private static bool IsResolvable(Ingredient ing) => !ing.IsEmpty;

	private static string StripNamespace(string id)
	{
		int colon = id.IndexOf(':');
		return (colon >= 0 ? id.Substring(colon + 1) : id).ToLowerInvariant();
	}
}
