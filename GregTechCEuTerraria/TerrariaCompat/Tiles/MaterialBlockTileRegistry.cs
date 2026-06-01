#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Common.Materials;
using GregTechCEuTerraria.TerrariaCompat.Items.Registry;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles;

// Registration table for material block tiles, keyed by upstream item id.
//
// Dump-driven: registers one MaterialBlockTile per MaterialBlockItem entry in
// the registry dump - storage blocks (`block` prefix, e.g. `iron_block`),
// raw-ore blocks (`rawOreBlock`, e.g. `raw_iron_block`) and material frames
// (`frame`, e.g. `steel_frame` - placed walk-through). A tile exists iff its
// block item exists, so a DUST-only material whose block item was synthesized
// (redstone, glowstone) gets a tile too - no INGOT/GEM predicate gate.
// MaterialItem looks up its own id here to wire DefaultToPlaceableTile.
public static class MaterialBlockTileRegistry
{
	private const string MaterialBlockItemClass = "com.gregtechceu.gtceu.api.item.MaterialBlockItem";

	private static readonly Dictionary<string, int> _tileTypeById = new();

	public static void Register(Mod mod)
	{
		_tileTypeById.Clear();
		foreach (var e in RegistryDump.Entries)
		{
			if (e.Class != MaterialBlockItemClass) continue;
			if (e.Prefix != "block" && e.Prefix != "rawOreBlock" && e.Prefix != "frame") continue;
			if (e.Material is null) continue;
			var material = MaterialRegistry.Get(e.Material);
			if (material is null) continue;

			// Frames are walk-through structural blocks (stand-on-top only);
			// storage / raw-ore blocks are fully solid.
			var tile = new MaterialBlockTile(e.BareId, material, walkThrough: e.Prefix == "frame");
			mod.AddContent(tile);
			_tileTypeById[e.BareId] = tile.Type;
		}
		mod.Logger.Info($"Registered {_tileTypeById.Count} material block tiles.");
	}

	// Looked up by MaterialItem with its own upstream id.
	public static int? Get(string blockItemId) =>
		_tileTypeById.TryGetValue(blockItemId, out var t) ? t : null;

	public static void Unload() => _tileTypeById.Clear();
}
