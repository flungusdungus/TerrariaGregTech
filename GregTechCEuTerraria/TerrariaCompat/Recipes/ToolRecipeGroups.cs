#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Items.Tools;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Recipes;

// One RecipeGroup per crafting-catalyst tool tag - so a recipe accepts any
// material's hammer/file/etc. Item lists from ToolItemLoader.CraftingTagItems
// (built at item-registration). Catalyst items get the not-consumed callback
// via ToolItemLoader.CatalystItemTypes (GregTech held-tool crafting).
public static class ToolRecipeGroups
{
	private static readonly HashSet<int> _catalystGroups = new();

	public static bool IsCatalystGroup(int groupId) => _catalystGroups.Contains(groupId);

	public static void Register()
	{
		_catalystGroups.Clear();

		foreach (var (tag, ids) in ToolItemLoader.CraftingTagItems)
		{
			if (ids.Count == 0) continue;

			// "gtceu:tools/crafting_wrenches" -> "Any wrench".
			string bare = tag[(tag.LastIndexOf('/') + 1)..].Replace("crafting_", "");
			string label = "Any " + bare.TrimEnd('s').Replace('_', ' ');

			int groupId = RecipeGroup.RegisterGroup(
				$"GregTechCEuTerraria:{tag}",
				new RecipeGroup(() => label, ids.ToArray()));

			VanillaItemMap.RegisterGroup(tag, groupId);
			_catalystGroups.Add(groupId);
		}
	}
}
