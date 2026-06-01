#nullable enable
using System.Collections.Generic;
using System.Linq;
using GregTechCEuTerraria.Common.Materials;
using GregTechCEuTerraria.Api.Data.Chemical.Material;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles;

// Registers one OreTile per Material with ORE form. Called from Mod.Load
// alongside MaterialItemRegistry so tile types are available for worldgen
// and KillTile drop resolution.
public static class OreTileRegistry
{
	// Terraria worlds are small (<= 8400x2400). Boosted per-block drop so a
	// single vein still yields enough material to play with.
	public const int RawOrePerBlock = 16;

	private static readonly Dictionary<string, OreTile> _tiles = new();

	public static int Count => _tiles.Count;

	public static ushort? Get(string materialId) =>
		_tiles.TryGetValue(materialId, out var t) ? t.Type : null;

	public static IEnumerable<ushort> AllTypes => _tiles.Values.Select(t => t.Type);

	public static IEnumerable<(string MaterialId, OreTile Tile)> All =>
		_tiles.Select(p => (p.Key, p.Value));

	public static void Register(Mod mod)
	{
		_tiles.Clear();
		foreach (var material in MaterialRegistry.All.Values)
		{
			if (!material.Forms.Contains("ORE")) continue;
			var tile = new OreTile(material);
			mod.AddContent(tile);
			_tiles[material.Id] = tile;
		}
		mod.Logger.Info($"Registered {_tiles.Count} ore tiles.");
	}

	public static void Unload() => _tiles.Clear();
}
