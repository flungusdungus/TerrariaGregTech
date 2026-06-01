#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.Api.Pattern;

// Port of com.gregtechceu.gtceu.api.pattern.predicates.PredicateBlocks.
//
// A SimplePredicate that matches if the current cell's tile is one of a
// given set. Used by Predicates.blocks(...) and Predicates.machines(...).
//
// Documented adaptations:
//   - `Block[]` -> `ushort[]` (Terraria tile types).
//   - The upstream sentinel `Blocks.BARRIER` ("never matches") is replaced
//     by an empty array - `Test(...)` returns false trivially.
//   - `candidates` returning `BlockInfo[]` -> `Func<Item[]>` that resolves
//     each tile type to its sibling item via name match. The convention
//     across our codebase (CasingTile / TieredMachineTile / etc.) is that
//     a tile and its drop item share the same `Name` - see registration
//     loops in CasingRegistry + TieredMachineFactory. The reverse lookup
//     is lazy + cached: built once on first PreviewItem() call.
public class PredicateBlocks : SimplePredicate
{
	public ushort[] TileTypes = Array.Empty<ushort>();

	public PredicateBlocks() : base("blocks") { }

	public PredicateBlocks(params ushort[] tileTypes) : this()
	{
		TileTypes = tileTypes;
		BuildPredicate();
	}

	public new SimplePredicate BuildPredicate()
	{
		TileTypes = TileTypes.Where(t => t > 0).ToArray();
		var types = TileTypes;
		Predicate = state =>
		{
			ushort current = state.GetTileType();
			if (current == 0 || Array.IndexOf(types, current) < 0) return false;
			// 2x2-tile alignment check - every shape cell maps to a 2x2
			// Terraria tile footprint, so the tile at the cell's anchor
			// position MUST be the top-left of its own placed multi-tile
			// group. Without this, a Style2x2 casing placed offset by 1
			// tile would still match the matcher's per-cell type check
			// (its right-cell tile-type is the same as its anchor), and
			// the player could form a misaligned multi. Skipped for 1x1
			// tiles - no multi-tile placement to validate.
			var tile = Terraria.Main.tile[state.PosX, state.PosY];
			var data = Terraria.ObjectData.TileObjectData.GetTileData(tile);
			if (data != null && (data.Width > 1 || data.Height > 1))
			{
				int subX = (tile.TileFrameX / 18) % data.Width;
				int subY = (tile.TileFrameY / 18) % data.Height;
				if (subX != 0 || subY != 0) return false;
			}
			return true;
		};
		Candidates = CandidatesForTiles(types);
		return this;
	}

	// Universal tile-types -> sibling-items resolver. Uses the codebase
	// convention that a tile and its drop item share the same `Name`
	// (CasingTile / TieredMachineTile / etc. all register via that pairing
	// - see CasingRegistry + TieredMachineFactory). Lazy + cached on first
	// call. Any predicate that wants ghost-preview candidates from a tile-
	// type list can call this.
	public static Func<Item[]> CandidatesForTiles(ushort[] tileTypes)
	{
		Item[]? cached = null;
		return () =>
		{
			if (cached != null) return cached;
			var list = new List<Item>(tileTypes.Length);
			foreach (var t in tileTypes)
			{
				var modTile = TileLoader.GetTile(t);
				if (modTile == null) continue;
				if (!modTile.Mod.TryFind<ModItem>(modTile.Name, out var modItem)) continue;
				var item = new Item();
				item.SetDefaults(modItem.Type);
				list.Add(item);
			}
			cached = list.ToArray();
			return cached;
		};
	}
}
