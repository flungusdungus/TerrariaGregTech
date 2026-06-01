#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Items.Registry;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Casings;

// Registers one placeable casing block - a CasingTile + CasingItem - per gtceu
// cube BlockItem in the registry dump. A dump entry is a casing iff its
// `render.texture` is set (snapshot-registry.py tags every BlockItem whose
// generated block model is a simple full cube; doors / fences / slabs / stairs
// / saplings / leaves are non-cube and get no texture, so they fall through).
//
// No per-block code: identity (id, name, texture, stack, rarity) is data. The
// casing items resolve in recipes via IngredientResolver's Mod.Find<ModItem>.
public static class CasingRegistry
{
	private const string BlockItemClass = "net.minecraft.world.item.BlockItem";

	public static int Count { get; private set; }

	public static void Register(Mod mod)
	{
		Count = 0;
		foreach (var e in RegistryDump.Entries)
		{
			if (e.BlockTexture is null) continue;
			if (e.Class != BlockItemClass) continue;
			// Defer to any dedicated tile/item already owning this id (avoids
			// tML's hard duplicate-name load failure).
			if (mod.TryFind<ModTile>(e.BareId, out _)) continue;
			if (mod.TryFind<ModItem>(e.BareId, out _)) continue;

			mod.AddContent(new CasingTile(e.BareId, e.BlockTexture, e.Name, e.ActiveBlockTexture));
			mod.AddContent(new CasingItem(e.BareId, e.BlockTexture, e.Name, e.MaxStack, e.Rarity));
			Count++;
		}

		mod.Logger.Info($"CasingRegistry: registered {Count} casing blocks from the registry dump.");
	}
}
