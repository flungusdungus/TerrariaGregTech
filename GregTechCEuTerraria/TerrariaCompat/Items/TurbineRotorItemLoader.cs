#nullable enable
using System.Collections.Generic;
using System.Text.RegularExpressions;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.Common.Materials;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items;

// One TurbineRotorItem per material with a rotor property (mirror of upstream
// TurbineRotorBehaviour.fillItemCategory). Run AFTER MaterialJsonLoader.
//
// Also installs the NBT-aware item-resolver hook
// (NBTPredicateIngredient.ResolveItemTypeFromNbt) so recipe refs to
// `gtceu:turbine_rotor` with an NBT payload land on the per-material ItemID.
public static class TurbineRotorItemLoader
{
	private static readonly Dictionary<string, int> _byMaterial = new();
	public static IReadOnlyDictionary<string, int> ByMaterial => _byMaterial;

	public static bool TryGetItemType(string materialId, out int itemType) =>
		_byMaterial.TryGetValue(materialId, out itemType);

	// Match `Material:"<id>"` inside a `GT.PartStats:{...}` block.
	// Whitespace-tolerant; accepts both `gtceu:aluminium` and bare `aluminium`.
	private static readonly Regex PartStatsMaterial = new(
		@"GT\.PartStats\s*:\s*\{\s*Material\s*:\s*""([^""]+)""",
		RegexOptions.Compiled);

	public static void Register(Mod mod)
	{
		_byMaterial.Clear();

		int registered = 0;
		foreach (var (id, material) in MaterialRegistry.All)
		{
			if (!material.HasRotor()) continue;

			var item = new TurbineRotorItem(material);
			mod.AddContent(item);
			_byMaterial[id] = item.Type;
			registered++;
		}

		NBTPredicateIngredient.ResolveItemTypeFromNbt = ResolveFromNbt;

		mod.Logger.Info($"TurbineRotorItemLoader: registered {registered} rotor items.");
	}

	public static void Unload()
	{
		_byMaterial.Clear();
		NBTPredicateIngredient.ResolveItemTypeFromNbt = null;
	}

	private static int ResolveFromNbt(string itemId, string snbt)
	{
		if (itemId != "gtceu:turbine_rotor") return 0;       // turbine_rotor only

		var m = PartStatsMaterial.Match(snbt);
		if (!m.Success) return 0;
		string raw = m.Groups[1].Value;
		int colon = raw.IndexOf(':');
		string bare = colon >= 0 ? raw[(colon + 1)..] : raw;
		return _byMaterial.TryGetValue(bare, out var t) ? t : 0;
	}
}
