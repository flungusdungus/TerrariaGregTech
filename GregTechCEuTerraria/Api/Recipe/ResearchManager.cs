#nullable enable
using System.Collections.Generic;
using Terraria;

namespace GregTechCEuTerraria.Api.Recipe;

// Port of com.gregtechceu.gtceu.utils.ResearchManager (the data-item +
// research-id surface the research subsystem needs).
//
// === Documented adaptations =================================================
//   - Item NBT (`assembly_line_research` CompoundTag) -> the
//     `ResearchDataGlobalItem` per-stack blob (Terraria has no native per-stack
//     NBT). research_id + research_type live there; this class is the
//     read/write facade so ported call-sites stay 1:1 with upstream.
//   - `isStackDataItem` IComponentItem/IDataItem behaviour lookup -> a static
//     {data_stick, data_orb, data_module} type check + the requireDataBank
//     table (data_module requires a Data Bank; verbatim with upstream's
//     DataItemBehavior flags).
//   - The `DataStickCopyScannerLogic` custom-recipe + default-research-recipe
//     builders are NOT ported (no GTRecipeBuilder DSL; research recipes come
//     pre-built in Data/Recipes/all.json).
public static class ResearchManager
{
	// Carrier struct for `(recipeType, researchId)` lookups.
	public readonly struct ResearchItem
	{
		public readonly GTRecipeType RecipeType;
		public readonly string       ResearchId;
		public ResearchItem(GTRecipeType recipeType, string researchId)
		{
			RecipeType = recipeType;
			ResearchId = researchId;
		}
	}

	// Items that require a Data Bank multiblock to function (upstream
	// DataItemBehavior(requireDataBank=true)). Only data_module.
	private static bool RequireDataBank(int type)
	{
		var mod = Terraria.ModLoader.ModContent.GetInstance<GregTechCEuTerraria>();
		return mod.TryFind<Terraria.ModLoader.ModItem>("data_module", out var mi) && mi.Type == type;
	}

	// True iff the item is a recognised research-data carrier. `isDataBank`
	// relaxes the requireDataBank gate (pass true if the caller IS a data bank
	// or the distinction doesn't matter). Verbatim with upstream's
	// `!dataItem.requireDataBank() || isDataBank`.
	public static bool IsStackDataItem(Item stack, bool isDataBank)
	{
		if (stack is null || stack.IsAir) return false;
		if (!TerrariaCompat.Items.ResearchDataGlobalItem.IsDataItemType(stack.type)) return false;
		return !RequireDataBank(stack.type) || isDataBank;
	}

	// Decode `(recipeType, researchId)` from the item's research blob.
	public static ResearchItem? ReadResearchId(Item stack)
	{
		if (stack is null || stack.IsAir) return null;
		if (!TerrariaCompat.Items.ResearchDataGlobalItem.IsDataItemType(stack.type)) return null;
		var blob = stack.GetGlobalItem<TerrariaCompat.Items.ResearchDataGlobalItem>();
		if (!blob.HasResearch) return null;
		var type = GTRecipeType.Get(StripNs(blob.ResearchType ?? ""));
		if (type is null) return null;
		return new ResearchItem(type, blob.ResearchId!);
	}

	// Stamp research data onto a data item (used by the research_station output).
	public static void WriteResearchToStack(Item stack, string researchId, GTRecipeType recipeType)
	{
		if (stack is null || stack.IsAir) return;
		var blob = stack.GetGlobalItem<TerrariaCompat.Items.ResearchDataGlobalItem>();
		blob.ResearchId   = researchId;
		blob.ResearchType = recipeType.RegistryName;
	}

	public static bool HasResearchTag(Item stack)
	{
		if (stack is null || stack.IsAir) return false;
		if (!TerrariaCompat.Items.ResearchDataGlobalItem.IsDataItemType(stack.type)) return false;
		return stack.GetGlobalItem<TerrariaCompat.Items.ResearchDataGlobalItem>().HasResearch;
	}

	// Look up the recipes a given research-id unlocks for a recipe type.
	public static IReadOnlyCollection<GTRecipe> GetRecipesFor(GTRecipeType recipeType, string researchId) =>
		recipeType.GetDataStickEntry(researchId) ?? System.Array.Empty<GTRecipe>();

	private static string StripNs(string id)
	{
		int i = id.IndexOf(':');
		return i >= 0 ? id[(i + 1)..] : id;
	}
}
