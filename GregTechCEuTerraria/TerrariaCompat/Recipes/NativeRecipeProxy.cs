#nullable enable
using System.Collections.Generic;
using System.Linq;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Category;
using GregTechCEuTerraria.Api.Recipe.Chance.Logic;
using GregTechCEuTerraria.Api.Recipe.Content;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using RecipeContent = GregTechCEuTerraria.Api.Recipe.Content.Content;
using TRecipe        = Terraria.Recipe;

namespace GregTechCEuTerraria.TerrariaCompat.Recipes;

// Two passes that fold "smelting-shaped" recipes into GT machine recipes:
//
// Pass A - every minecraft:smelting recipe re-emitted as an electric_furnace
// recipe at ULV (EUt = V[ULV]/2 = 4). Verbatim with GTRecipeTypes:82
// register("electric_furnace", ELECTRIC, RecipeType.SMELTING) (which uses a
// proxy-recipe mechanism in RecipeManagerHandler). Without it, the
// electric_furnace station has 0 recipes and the coverage check flags it.
//
// Pass B - every Terraria-tile recipe whose tile is in TileToStation is
// re-emitted as a GT recipe at the corresponding tier. EUt = V[tier]/2 sits
// inside the tier's range and locks lower-tier machines out. Bridge-tracked
// recipes are skipped so JSON-sourced recipes aren't mirrored back as natives.
// Duration: 50 ticks (~0.83s, project balance).
public static class NativeRecipeProxy
{
	private const int SynthDuration = 50;

	// tier >= 0: minimum machine-tier gate (EUt = V[tier]/2).
	// tier == -1 (CraftingTier): no EU, instant-craft sentinel for vanilla
	// crafting tiles - synthesized recipes look like crafting_shapeless.
	private const int CraftingTier = -1;
	private static readonly Dictionary<int, (string Station, int Tier)> TileToStation = new()
	{
		{ TileID.Furnaces,        ("electric_furnace", 0) },   // ULV
		{ TileID.Hellforge,       ("electric_furnace", 2) },   // MV
		{ TileID.AdamantiteForge, ("electric_furnace", 4) },   // EV
		{ TileID.LihzahrdFurnace, ("electric_furnace", 6) },   // LuV
		{ TileID.GlassKiln,       ("arc_furnace",      1) },   // LV
		{ TileID.BoneWelder,      ("forming_press",    1) },   // LV
		{ TileID.HoneyDispenser,  ("canner",           2) },   // MV
		{ TileID.DemonAltar,      ("circuit_assembler", 1) },  // LV
		{ TileID.WorkBenches,     ("crafting_shapeless", CraftingTier) },
	};

	// No-tile recipes share the workbench station - both are shapeless crafting.
	private const string HandCraftingStation = "crafting_shapeless";

	// Fallback station name for tiles not in TileToStation - TileID.Anvils ->
	// "anvils", TileID.LunarCraftingStation -> "lunar_crafting_station". Modded
	// tiles not in TileID.Search fall back to `tile_<id>`.
	private static readonly Dictionary<int, string> _stationByTileCache = new();
	private static string GenericStationForTile(int tile)
	{
		if (_stationByTileCache.TryGetValue(tile, out var name)) return name;
		string raw = TileID.Search.TryGetName(tile, out var found) ? found : $"tile_{tile}";
		name = PascalToSnake(raw);
		_stationByTileCache[tile] = name;
		return name;
	}

	private static string PascalToSnake(string s)
	{
		var sb = new System.Text.StringBuilder(s.Length + 4);
		for (int i = 0; i < s.Length; i++)
		{
			char c = s[i];
			if (char.IsUpper(c) && i > 0 && (char.IsLower(s[i - 1]) || (i + 1 < s.Length && char.IsLower(s[i + 1]))))
				sb.Append('_');
			sb.Append(char.ToLowerInvariant(c));
		}
		return sb.ToString();
	}

	// Pass A - called from RecipeJsonLoader.Load BEFORE RecipeRegistry.Set so
	// we mutate the in-flight byStation dict directly.
	public static void SynthesizeFromSmelting(Dictionary<string, List<GTRecipe>> byStation)
	{
		if (!byStation.TryGetValue("smelting", out var sources) || sources.Count == 0) return;

		var efRecipeType = GTRecipeType.GetOrCreate("electric_furnace");
		var ulvEUt       = Common.Energy.VoltageTiers.V(0) / 2;   // = 4

		if (!byStation.TryGetValue("electric_furnace", out var efList))
		{
			efList = new List<GTRecipe>();
			byStation["electric_furnace"] = efList;
		}

		int n = 0;
		foreach (var src in sources)
		{
			// Skip recipes that didn't resolve an output - TagIngredient with 0
			// types, etc. The output map is empty in that case.
			if (src.Outputs.Count == 0 || src.Inputs.Count == 0) continue;

			efList.Add(BuildSynthetic(
				efRecipeType,
				id:       $"electric_furnace/proxy/{src.Id}",
				inputs:   CloneContents(src.Inputs),
				outputs:  CloneContents(src.Outputs),
				eutInput: ulvEUt));
			n++;
		}

		ModContent.GetInstance<GregTechCEuTerraria>().Logger
			.Info($"NativeRecipeProxy: synthesized {n} electric_furnace recipes from smelting bundle.");
	}

	// === Pass B - Terraria native recipes -> GT machine recipes ================
	// Called from PostAddRecipes (after Main.recipe[] is populated).
	public static void SynthesizeFromTerrariaRecipes()
	{
		var per = new Dictionary<string, List<GTRecipe>>();

		int scanned = 0, synth = 0, skippedBridged = 0;
		for (int r = 0; r < TRecipe.numRecipes; r++)
		{
			var rec = Main.recipe[r];
			if (rec is null) continue;

			// Skip bridge-pushed recipes - already in RecipeRegistry under
			// their original station; re-mirroring would dupe every workbench
			// recipe in the browser.
			if (VanillaCraftingBridge.BridgeRegistered.Contains(rec))
			{
				skippedBridged++;
				continue;
			}

			// First mapped tile wins (multi-tile recipes are alternatives, not
			// requirements). No-tile -> shapeless station.
			(string Station, int Tier)? mapping = null;
			for (int t = 0; t < rec.requiredTile.Count; t++)
			{
				int tile = rec.requiredTile[t];
				if (tile <= 0) continue;
				if (TileToStation.TryGetValue(tile, out var m)) { mapping = m; break; }
			}
			if (mapping is null)
			{
				// Fallback to tile-NAME station - mirrors every Terraria recipe
				// into the browser without per-tile hardcoding.
				int firstTile = -1;
				for (int t = 0; t < rec.requiredTile.Count; t++)
					if (rec.requiredTile[t] > 0) { firstTile = rec.requiredTile[t]; break; }
				string station = firstTile < 0
					? HandCraftingStation
					: GenericStationForTile(firstTile);
				mapping = (station, CraftingTier);
			}
			scanned++;

			if (rec.createItem.IsAir) continue;     // 1 createItem per Terraria recipe
			var outputs = new Dictionary<object, List<RecipeContent>>
			{
				[ItemRecipeCapability.CAP] = new List<RecipeContent>
				{
					ItemContent(new ItemStackIngredient(rec.createItem.type), rec.createItem.stack),
				},
			};

			// RecipeGroup slots -> TagIngredient over every group member.
			var inputs = new Dictionary<object, List<RecipeContent>>();
			var itemInputs = new List<RecipeContent>();
			bool ok = true;
			for (int i = 0; i < rec.requiredItem.Count; i++)
			{
				var req = rec.requiredItem[i];
				if (req is null || req.IsAir) continue;
				int count = req.stack;

				Ingredient ing;
				if (TryBuildGroupedIngredient(rec, req.type, out var grouped))
					ing = grouped;
				else
					ing = new ItemStackIngredient(req.type);

				itemInputs.Add(ItemContent(ing, count));
			}
			if (!ok || itemInputs.Count == 0) continue;
			inputs[ItemRecipeCapability.CAP] = itemInputs;

			// Crafting tier (-1): no EU/instant. Tier >= 0: EUt = V[tier]/2 +
			// SynthDuration so the recipe runs on a real GT machine too.
			var tickInputs = new Dictionary<object, List<RecipeContent>>();
			int duration = 0;
			if (mapping.Value.Tier >= 0)
			{
				long eut = Common.Energy.VoltageTiers.V(mapping.Value.Tier) / 2;
				EURecipeCapability.PutEUContent(tickInputs, new EnergyStack(eut, 1));
				duration = SynthDuration;
			}

			// Source tile id for the recipe-browser's second-station chip - so
			// "2 Clay -> 1 Red Brick" under electric_furnace also surfaces the
			// vanilla Furnaces path. Browser only shows the chip when the
			// native tile resolves to a different icon than the GT station.
			var data = new TagCompound();
			int sourceTile = -1;
			for (int t = 0; t < rec.requiredTile.Count; t++)
				if (rec.requiredTile[t] > 0) { sourceTile = rec.requiredTile[t]; break; }
			if (sourceTile > 0) data.Set("nativeTile", sourceTile);

			var gtType = GTRecipeType.GetOrCreate(mapping.Value.Station);
			var synthetic = new GTRecipe(
				recipeType:              gtType,
				id:                      $"{mapping.Value.Station}/native/{r}",
				inputs:                  inputs,
				outputs:                 outputs,
				tickInputs:              tickInputs,
				tickOutputs:             new Dictionary<object, List<RecipeContent>>(),
				inputChanceLogics:       new Dictionary<object, ChanceLogic>(),
				outputChanceLogics:      new Dictionary<object, ChanceLogic>(),
				tickInputChanceLogics:   new Dictionary<object, ChanceLogic>(),
				tickOutputChanceLogics:  new Dictionary<object, ChanceLogic>(),
				conditions:              new List<RecipeCondition>(),
				ingredientActions:       System.Array.Empty<object>(),
				data:                    data,
				duration:                duration,
				recipeCategory:          GTRecipeCategory.DEFAULT,
				groupColor:              -1);

			if (!per.TryGetValue(mapping.Value.Station, out var bucket))
			{
				bucket = new List<GTRecipe>();
				per[mapping.Value.Station] = bucket;
			}
			bucket.Add(synthetic);
			VanillaCraftingBridge.GTToVanilla[synthetic] = rec;
			synth++;
		}

		RecipeRegistry.AppendAll(per);

		ModContent.GetInstance<GregTechCEuTerraria>().Logger
			.Info($"NativeRecipeProxy: synthesized {synth}/{scanned} Terraria native recipes " +
			      $"across {per.Count} GT stations" +
			      (skippedBridged > 0 ? $" (skipped {skippedBridged} bridge-registered)" : "") + ".");
	}

	// Group-slot -> TagIngredient over every group member; else false (caller
	// falls back to ItemStackIngredient).
	private static bool TryBuildGroupedIngredient(TRecipe rec, int slotItemType, out TagIngredient grouped)
	{
		grouped = null!;
		foreach (int gid in rec.acceptedGroups)
		{
			if (gid <= 0 || !RecipeGroup.recipeGroups.TryGetValue(gid, out var group)) continue;
			var members = group.ValidItems;
			if (members is null || !members.Contains(slotItemType)) continue;

			var list = members.ToList();
			grouped = new TagIngredient($"$terraria:group/{gid}", list);
			return true;
		}
		return false;
	}

	// Pass-A builder (per-tick EU + items @ full chance). Pass B builds inline.
	private static GTRecipe BuildSynthetic(GTRecipeType type, string id,
		Dictionary<object, List<RecipeContent>> inputs,
		Dictionary<object, List<RecipeContent>> outputs,
		long eutInput)
	{
		var tickInputs = new Dictionary<object, List<RecipeContent>>();
		EURecipeCapability.PutEUContent(tickInputs, new EnergyStack(eutInput, 1));

		return new GTRecipe(
			recipeType:              type,
			id:                      id,
			inputs:                  inputs,
			outputs:                 outputs,
			tickInputs:              tickInputs,
			tickOutputs:             new Dictionary<object, List<RecipeContent>>(),
			inputChanceLogics:       new Dictionary<object, ChanceLogic>(),
			outputChanceLogics:      new Dictionary<object, ChanceLogic>(),
			tickInputChanceLogics:   new Dictionary<object, ChanceLogic>(),
			tickOutputChanceLogics:  new Dictionary<object, ChanceLogic>(),
			conditions:              new List<RecipeCondition>(),
			ingredientActions:       System.Array.Empty<object>(),
			data:                    new TagCompound(),
			duration:                SynthDuration,
			recipeCategory:          GTRecipeCategory.DEFAULT,
			groupColor:              -1);
	}

	private static RecipeContent ItemContent(Ingredient ing, int count)
	{
		int max     = ChanceLogic.GetMaxChancedValue();
		Ingredient payload = count > 1 ? SizedIngredient.Create(ing, count) : ing;
		return new RecipeContent(payload, max, max, 0);
	}

	// Shallow copy - Content payloads are immutable Ingredient objects.
	private static Dictionary<object, List<RecipeContent>> CloneContents(
		Dictionary<object, List<RecipeContent>> src)
	{
		var clone = new Dictionary<object, List<RecipeContent>>(src.Count);
		foreach (var (k, v) in src) clone[k] = new List<RecipeContent>(v);
		return clone;
	}
}
