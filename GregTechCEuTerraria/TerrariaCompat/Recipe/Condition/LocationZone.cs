#nullable enable
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Recipe.Condition;

// Position-based zone detection helpers used by recipe conditions to check
// the MACHINE'S world location (not the local player's). Terraria's vanilla
// Zone* flags live on Player and are updated each tick by Player.UpdateBiomes;
// they're not directly queryable for an arbitrary world position.
//
// We replicate the relevant tile-Y / tile-scan checks here. Heavy scans
// (biome detection) cache results per-tick - recipe tick is server-side
// and idempotent within a tick, so a position->biome map can be stashed
// per logic tick.
internal static class LocationZone
{
	// Vanilla Terraria altitude bands (matches Player.UpdateBiomes()):
	//   ZoneSkyHeight       : y < Main.worldSurface * 0.35
	//   ZoneOverworldHeight : Main.worldSurface * 0.35 <= y <= Main.worldSurface
	//   ZoneDirtLayerHeight : Main.worldSurface < y <= Main.rockLayer
	//   ZoneRockLayerHeight : Main.rockLayer < y <= Main.UnderworldLayer
	//   ZoneUnderworldHeight: y >= Main.UnderworldLayer
	//
	// Y in Terraria grows DOWNWARD: small Y = high altitude (space).

	public static bool IsSpace(int tileY)      => tileY < Main.worldSurface * 0.35;
	public static bool IsOverworld(int tileY)  => !IsSpace(tileY) && !IsUnderworld(tileY);
	public static bool IsUnderground(int tileY)=> tileY > Main.worldSurface && tileY <= Main.rockLayer;
	public static bool IsCavern(int tileY)     => tileY > Main.rockLayer && tileY < Main.UnderworldLayer;
	public static bool IsUnderworld(int tileY) => tileY >= Main.UnderworldLayer;

	// Scan a tile-radius around (cx, cy) for any tile type in `biomeTiles`.
	// Returns true if the count meets `threshold`. Used by biome detection
	// (jungle, desert, snow...) where biomes are defined by tile-count
	// thresholds (matching Terraria's vanilla biome-counting logic).
	//
	// `radius` defaults to 35 tiles which matches Terraria's vanilla
	// SceneMetrics scan radius for biome detection.
	public static bool ScanForBiomeTiles(int cx, int cy, ushort[] biomeTiles, int threshold = 80, int radius = 35)
	{
		int count = 0;
		int xMin = System.Math.Max(0, cx - radius);
		int xMax = System.Math.Min(Main.maxTilesX - 1, cx + radius);
		int yMin = System.Math.Max(0, cy - radius);
		int yMax = System.Math.Min(Main.maxTilesY - 1, cy + radius);
		for (int x = xMin; x <= xMax; x++)
		{
			for (int y = yMin; y <= yMax; y++)
			{
				var tile = Main.tile[x, y];
				if (!tile.HasTile) continue;
				foreach (var bt in biomeTiles)
				{
					if (tile.TileType == bt) { count++; break; }
				}
				if (count >= threshold) return true;
			}
		}
		return false;
	}
}
