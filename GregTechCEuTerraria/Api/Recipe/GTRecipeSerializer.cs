#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Recipe.Category;
using GregTechCEuTerraria.Api.Recipe.Chance.Logic;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.Common.Recipe.Condition;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Recipe;

// LOCKED - port of com.gregtechceu.gtceu.api.recipe.GTRecipeSerializer.
// Adapted to System.Text.Json (vs upstream's Mojang Codec).
//
// Reads upstream's native recipe JSON (produced by `./gradlew runData`) into
// a GTRecipe instance. The wire shape:
//
// {
//   "type": "gtceu:macerator",
//   "duration": 200, "parallels": 1, "subtickParallels": 1, "batchParallels": 1,
//   "groupColor": -1,
//   "category": "gtceu:ore_processing",
//   "data": {...},                                  // optional CompoundTag
//   "inputs":  { "<cap-id>": [Content, ...], ... }, // typed-content per capability
//   "outputs": { ... },
//   "tickInputs": { ... },
//   "tickOutputs": { ... },
//   "inputChanceLogics":  { "<cap-id>": "<chance-logic-id>", ... },
//   "outputChanceLogics": { ... },
//   "tickInputChanceLogics":  { ... },
//   "tickOutputChanceLogics": { ... },
//   "recipeConditions": [ {RecipeCondition}, ... ],
//   "ingredientActions": [...]                       // KubeJS (dropped)
// }
//
// Each Content entry:
//   { "content": {...payload}, "chance": 10000, "maxChance": 10000, "tierChanceBoost": 0 }
//
// The "content" payload type depends on the capability:
//   minecraft:item  -> Ingredient JSON (IngredientJson.Read)
//   minecraft:fluid -> FluidIngredient JSON
//   gtceu:eu        -> EnergyStack JSON (voltage + amperage)
//
// Documented adaptations:
//   - Mojang Codec dispatch dropped; System.Text.Json switch by `type` field.
//   - GTRegistries.RECIPE_CAPABILITIES / CHANCE_LOGICS lookup -> static
//     dispatch by capability/logic name (returns null on unknown so older /
//     newer JSON degrades gracefully).
//   - KubeJS `ingredientActions` parsed as empty list.
//   - FriendlyByteBuf network read/write deferred (recipe sync packets come later).
public static class GTRecipeSerializer
{
	public static GTRecipe Read(JsonElement root, IIngredientResolver resolver, string id, string? stationOverride = null)
	{
		string typeStr = root.TryGetProperty("type", out var typeEl) ? (typeEl.GetString() ?? "") : "";
		string station = stationOverride ?? typeStr;
		// Strip "gtceu:" or whichever namespace prefix from the recipe type
		// when materializing the GTRecipeType key.
		int colon = station.IndexOf(':');
		if (colon >= 0) station = station[(colon + 1)..];
		var recipeType = GTRecipeType.GetOrCreate(station);

		int duration         = GetInt(root, "duration", 200);
		int parallels        = GetInt(root, "parallels", 1);
		int subtickParallels = GetInt(root, "subtickParallels", 1);
		int batchParallels   = GetInt(root, "batchParallels", 1);
		int ocLevel          = GetInt(root, "ocLevel", 0);
		int groupColor       = GetInt(root, "groupColor", -1);

		var data = root.TryGetProperty("data", out var dataEl) ? ReadTag(dataEl) : new TagCompound();

		var inputs       = ReadCapMap(root, "inputs",      resolver);
		var outputs      = ReadCapMap(root, "outputs",     resolver);
		var tickInputs   = ReadCapMap(root, "tickInputs",  resolver);
		var tickOutputs  = ReadCapMap(root, "tickOutputs", resolver);

		var inputChanceLogics       = ReadChanceLogicMap(root, "inputChanceLogics");
		var outputChanceLogics      = ReadChanceLogicMap(root, "outputChanceLogics");
		var tickInputChanceLogics   = ReadChanceLogicMap(root, "tickInputChanceLogics");
		var tickOutputChanceLogics  = ReadChanceLogicMap(root, "tickOutputChanceLogics");

		var conditions = ReadConditions(root);

		var category = ReadCategory(root, recipeType);
		var rawCategoryId = root.TryGetProperty("category", out var catEl) && catEl.ValueKind == JsonValueKind.String
			? catEl.GetString()
			: null;

		var recipe = new GTRecipe(
			recipeType:               recipeType,
			id:                       id,
			inputs:                   inputs,
			outputs:                  outputs,
			tickInputs:               tickInputs,
			tickOutputs:              tickOutputs,
			inputChanceLogics:        inputChanceLogics,
			outputChanceLogics:       outputChanceLogics,
			tickInputChanceLogics:    tickInputChanceLogics,
			tickOutputChanceLogics:   tickOutputChanceLogics,
			conditions:               conditions,
			ingredientActions:        System.Array.Empty<object>(),  // KubeJS dropped
			data:                     data,
			duration:                 duration,
			recipeCategory:           category,
			groupColor:               groupColor);

		// Upstream applies these AFTER ctor (not in the ctor signature) -
		// matches `copy()` which copies these flags onto the new instance.
		recipe.CategoryId       = rawCategoryId;
		recipe.Parallels        = parallels;
		recipe.SubtickParallels = subtickParallels;
		recipe.BatchParallels   = batchParallels;
		recipe.OcLevel          = ocLevel;
		return recipe;
	}

	// === Per-capability content map parsing ==================================

	private static Dictionary<object, List<Content.Content>> ReadCapMap(
		JsonElement root, string key, IIngredientResolver resolver)
	{
		var result = new Dictionary<object, List<Content.Content>>();
		if (!root.TryGetProperty(key, out var mapEl) || mapEl.ValueKind != JsonValueKind.Object)
			return result;
		foreach (var capEntry in mapEl.EnumerateObject())
		{
			object? cap = ResolveCapability(capEntry.Name);
			if (cap is null) continue; // unknown capability - skip
			var contents = new List<Content.Content>();
			foreach (var contentEl in capEntry.Value.EnumerateArray())
				contents.Add(ReadContent(contentEl, cap, resolver));
			result[cap] = contents;
		}
		return result;
	}

	// Upstream Content shape:
	//   {"content": {...payload}, "chance": N, "maxChance": N, "tierChanceBoost": N}
	// Payload dispatch by capability.
	private static Content.Content ReadContent(JsonElement el, object cap, IIngredientResolver resolver)
	{
		int maxChance = ChanceLogic.GetMaxChancedValue();
		int chance          = GetInt(el, "chance",          maxChance);
		int maxChanceField  = GetInt(el, "maxChance",       maxChance);
		int tierChanceBoost = GetInt(el, "tierChanceBoost", 0);

		object payload = ReadContentPayload(el.GetProperty("content"), cap, resolver);
		return new Content.Content(payload, chance, maxChanceField, tierChanceBoost);
	}

	private static object ReadContentPayload(JsonElement payload, object cap, IIngredientResolver resolver)
	{
		// Dispatch by capability identity. ItemRecipeCapability -> ingredient
		// JSON. FluidRecipeCapability -> fluid ingredient (subset of
		// IngredientJson with `fluid`/`tag`/`attribute` shape).
		// EURecipeCapability -> EnergyStack (voltage + amperage).
		if (ReferenceEquals(cap, ItemRecipeCapability.CAP))
			return IngredientJson.Read(payload, resolver);
		// Fluid-cap content has its own wire shape ({"amount","value"}) -
		// upstream parses it with FluidIngredient.CODEC, not the item-side
		// Ingredient dispatch. Route straight to the fluid parser.
		if (ReferenceEquals(cap, FluidRecipeCapability.CAP))
			return IngredientJson.ReadFluidIngredient(payload, resolver);
		if (ReferenceEquals(cap, EURecipeCapability.CAP))
		{
			// Upstream serializes EU as either:
			//   - bare int (voltage only; amperage defaults to 1) - common case
			//   - object {voltage, amperage} - rare, for non-1A recipes
			if (payload.ValueKind == JsonValueKind.Number)
				return new Ingredient.EnergyStack(payload.GetInt64(), 1L);
			long voltage  = GetLong(payload, "voltage",  0L);
			long amperage = GetLong(payload, "amperage", 1L);
			return new Ingredient.EnergyStack(voltage, amperage);
		}
		if (ReferenceEquals(cap, CWURecipeCapability.CAP))
		{
			// CWU content is a bare int (CWU/t). Upstream's SerializerInteger
			// round-trips a plain integer; our payload is the JSON number.
			return payload.ValueKind == JsonValueKind.Number ? payload.GetInt32() : 0;
		}
		// Unknown capability payload - return raw JsonElement clone for
		// future inspection (matches upstream's "store as Object" semantics
		// at the wildcard level).
		return payload.Clone();
	}

	// === Chance-logic map parsing ============================================

	private static Dictionary<object, ChanceLogic> ReadChanceLogicMap(JsonElement root, string key)
	{
		var result = new Dictionary<object, ChanceLogic>();
		if (!root.TryGetProperty(key, out var mapEl) || mapEl.ValueKind != JsonValueKind.Object)
			return result;
		foreach (var entry in mapEl.EnumerateObject())
		{
			object? cap = ResolveCapability(entry.Name);
			if (cap is null) continue;
			string logicName = entry.Value.GetString() ?? "or";
			var logic = ResolveChanceLogic(logicName);
			if (logic is not null) result[cap] = logic;
		}
		return result;
	}

	// === Conditions list =====================================================

	private static List<RecipeCondition> ReadConditions(JsonElement root)
	{
		var result = new List<RecipeCondition>();
		// Upstream's GTRecipe codec field is `recipeConditions` - accept the
		// legacy `conditions` spelling too for hand-authored compat recipes.
		if ((!root.TryGetProperty("recipeConditions", out var arr) || arr.ValueKind != JsonValueKind.Array) &&
		    (!root.TryGetProperty("conditions", out arr)          || arr.ValueKind != JsonValueKind.Array))
			return result;
		foreach (var el in arr.EnumerateArray())
		{
			var cond = RecipeConditionJson.Read(el);
			if (cond is not null) result.Add(cond);
		}
		return result;
	}

	// === Category lookup =====================================================

	private static GTRecipeCategory ReadCategory(JsonElement root, GTRecipeType recipeType)
	{
		// Upstream serializes as `"category": "gtceu:some_category"`.
		// We don't ship a category registry yet - fall back to the recipe
		// type's default category. The string name is preserved in case
		// future browser code wants to use it for grouping.
		_ = recipeType;
		return GTRecipeCategory.DEFAULT;
	}

	// === Registries ==========================================================
	// Static lookups in lieu of upstream's GTRegistries.

	private static object? ResolveCapability(string id) => StripNs(id) switch
	{
		"item"  => ItemRecipeCapability.CAP,
		"fluid" => FluidRecipeCapability.CAP,
		"eu"    => EURecipeCapability.CAP,
		"cwu"   => CWURecipeCapability.CAP,
		_ => null,
	};

	private static ChanceLogic? ResolveChanceLogic(string id) => StripNs(id) switch
	{
		"or"   => ChanceLogic.OR,
		"and"  => ChanceLogic.AND,
		"xor"  => ChanceLogic.XOR,
		"none" => ChanceLogic.NONE,
		_ => null,
	};

	// === Helpers =============================================================

	private static string StripNs(string s)
	{
		int idx = s.IndexOf(':');
		return idx < 0 ? s : s[(idx + 1)..];
	}

	private static int GetInt(JsonElement el, string key, int def) =>
		el.TryGetProperty(key, out var v) ? v.GetInt32() : def;
	private static long GetLong(JsonElement el, string key, long def) =>
		el.TryGetProperty(key, out var v) ? v.GetInt64() : def;

	// Shallow JSON -> TagCompound mapper. Used for the `data` field -
	// scalar values land typed; nested objects/arrays preserved as JSON
	// strings (most recipes don't use complex `data` shapes).
	private static TagCompound ReadTag(JsonElement el)
	{
		var tag = new TagCompound();
		if (el.ValueKind != JsonValueKind.Object) return tag;
		foreach (var kv in el.EnumerateObject())
		{
			tag[kv.Name] = kv.Value.ValueKind switch
			{
				JsonValueKind.Number when kv.Value.TryGetInt32(out var i)  => (object)i,
				JsonValueKind.Number when kv.Value.TryGetInt64(out var l)  => l,
				JsonValueKind.Number when kv.Value.TryGetDouble(out var d) => d,
				JsonValueKind.True                                          => true,
				JsonValueKind.False                                         => false,
				JsonValueKind.String                                        => kv.Value.GetString() ?? string.Empty,
				_                                                           => kv.Value.GetRawText(),
			};
		}
		return tag;
	}
}
