#nullable enable
using System.Collections.Generic;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Armor;

// Registers the GregTech power-armor pieces from ArmorCatalog and records their
// upstream-id -> ItemType mapping so recipe refs resolve through IngredientResolverImpl.
// Must run BEFORE RegistryItemLoader (resolver ordering / TryFind dedup).
// Not registered: boots; Advanced chestplates / jetpacks / hazmat / nightvision goggles.
public static class ArmorItemLoader
{
	// "gtceu:<id>" -> Terraria ItemType.
	private static readonly Dictionary<string, int> _byUpstreamId = new();
	public static IReadOnlyDictionary<string, int> ByUpstreamId => _byUpstreamId;

	public static bool TryGet(string upstreamId, out int itemType) =>
		_byUpstreamId.TryGetValue(upstreamId, out itemType);

	public static void Register(Mod mod)
	{
		_byUpstreamId.Clear();

		// Set-bonus tooltip text (read by GTArmorItem.UpdateArmorSet).
		Terraria.Localization.Language.GetOrRegister("Mods.GregTechCEuTerraria.ArmorSet.Nano",
			() => "Set bonus: chance to dodge attacks, +10% movement speed");
		Terraria.Localization.Language.GetOrRegister("Mods.GregTechCEuTerraria.ArmorSet.Quark",
			() => "Set bonus: 12% damage reduction, +6 defense");

		foreach (var spec in ArmorCatalog.All)
		{
			var item = new GTArmorItem(spec);
			mod.AddContent(item);
			_byUpstreamId["gtceu:" + spec.Id] = item.Type;
		}
		mod.Logger.Info($"ArmorItemLoader: registered {ArmorCatalog.All.Count} power-armor pieces.");
	}

	public static void Unload() => _byUpstreamId.Clear();
}
