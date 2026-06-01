#nullable enable
using System.Collections.Generic;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Fluids;

// Maps upstream empty-cell ids to Terraria ItemIDs for IngredientResolver
// (fluid_cell, steel_fluid_cell, ...). Filled-cell refs (`<fluid>_cell`) are
// deferred - would need (type + NBT) return rather than int (no shipped
// recipes use them today).
public static class FluidCellRegistry
{
	private static readonly Dictionary<string, int> _byUpstreamId = new();

	internal static void Register(Mod mod)
	{
		foreach (var id in new[]
		{
			"fluid_cell", "universal_fluid_cell",
			"steel_fluid_cell", "aluminium_fluid_cell", "stainless_steel_fluid_cell",
			"titanium_fluid_cell", "tungsten_steel_fluid_cell",
		})
		{
			if (mod.TryFind<ModItem>(id, out var modItem))
				_byUpstreamId[id] = modItem.Type;
		}
	}

	public static bool TryGet(string upstreamId, out int itemType) =>
		_byUpstreamId.TryGetValue(upstreamId, out itemType);
}
