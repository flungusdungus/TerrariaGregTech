#nullable enable
using System.Collections.Generic;
using System.Globalization;
using GregTechCEuTerraria.Api.Data.Chemical.Material;
using GregTechCEuTerraria.Api.Tool;
using GregTechCEuTerraria.Common.Materials;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Tools;

// One GregithItem per full-toolset metallic/alloy material. MinTools=8 admits
// every metallic + alloy (18-19 non-electric tools each) and rejects partial
// sets (flint=7, wood=1, rubber=0). MUST run AFTER ToolItemLoader.Register.
public static class GregithItemLoader
{
	private const int MinTools = 8;

	private static readonly Dictionary<string, int> _byMaterialId = new();
	public static IReadOnlyDictionary<string, int> ByMaterialId => _byMaterialId;

	public static void Register(Mod mod)
	{
		_byMaterialId.Clear();
		int registered = 0;

		// For the Overclocked Gregith assembled below: recipe = one of each
		// Gregith; visual/projectile pool = every Gregith's tool set.
		var allGregithTypes = new List<int>();
		var allToolPool = new List<int>();
		Material? overclockMaterial = null;

		foreach (var (_, material) in MaterialRegistry.All)
		{
			if (!material.HasTool()) continue;

			var ingredients = new List<int>();
			foreach (var typeName in material.Tool!.Types)
			{
				var type = GTToolType.Get(typeName);
				if (type == null || type.IsElectric) continue;
				if (type == GTToolType.MORTAR) continue;                // user request
				string toolId = $"gtceu:{type.ResolveId(material.Id)}";
				if (ToolItemLoader.TryGet(toolId, out int itemType))
					ingredients.Add(itemType);
			}

			if (ingredients.Count < MinTools) continue;

			int tier = ToolTier.For(material);
			string id = $"{material.Id}_gregith";
			string label = $"{TitleCase(material.Id)} Gregith";

			var item = new GregithItem(id, label, material, tier, ingredients.ToArray());
			mod.AddContent(item);
			_byMaterialId[material.Id] = item.Type;
			registered++;

			allGregithTypes.Add(item.Type);
			allToolPool.AddRange(ingredients);
			// Prefer neutronium (tier 9 - anchors Overclocked's damage doubling).
			if (material.Id == "neutronium" || overclockMaterial == null)
				overclockMaterial = material;

			// Catalyst for every tool tag - runs BEFORE ToolRecipeGroups.Register
			// so per-tag RecipeGroups pick this type up.
			ToolItemLoader.RegisterAsCatalystForAllTags(item.Type);
		}

		// Overclocked Gregith pinned at tier 9 so the orbit reach matches
		// Neutronium; GregithItem applies the 2x damage / 4x shot count.
		if (allGregithTypes.Count >= 2 && overclockMaterial != null)
		{
			const int overclockTier = ToolTier.TierCount - 1; // 9 = UHV+
			var oc = new GregithItem("overclocked_gregith", "Overclocked Gregith",
				overclockMaterial, overclockTier, allToolPool.ToArray(),
				overclocked: true, recipeItemTypes: allGregithTypes.ToArray());
			mod.AddContent(oc);
			_byMaterialId["overclocked"] = oc.Type;
			registered++;
			ToolItemLoader.RegisterAsCatalystForAllTags(oc.Type);
		}

		mod.Logger.Info($"GregithItemLoader: registered {registered} Gregith weapons.");
	}

	public static void Unload() => _byMaterialId.Clear();

	private static string TitleCase(string id) =>
		CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('_', ' '));
}
