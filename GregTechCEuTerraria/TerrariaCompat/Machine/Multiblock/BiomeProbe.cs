#nullable enable
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.WorldBuilding;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

// Server-safe biome lookup at world-tile (x, y). Walks the buff-scan rectangle,
// counts TileID.Sets.*Biome[] signatures, applies SceneMetrics thresholds +
// hallow/evil/blood cancellation. Identical math to Player.UpdateBiomes but
// no Player needed (tML's SceneMetrics has no Zone* flags - player-centric).
// Resolution order (matters when zones overlap): Underworld -> Ocean ->
// Mushroom -> Jungle -> Snow -> Hallow -> Crimson -> Corruption -> Desert -> Forest.
public static class BiomeProbe
{
	public enum Biome
	{
		Forest, Desert, Snow, Jungle, Ocean, Mushroom,
		Crimson, Corruption, Hallow, Underworld,
		Cavern,   // reserved; probe currently returns Forest for default.
	}

	// Vanilla SceneMetrics thresholds - hardcoded to insulate from silent drift.
	private const int CorruptionTileThreshold = 300;
	private const int CrimsonTileThreshold    = 300;
	private const int HallowTileThreshold     = 125;
	private const int JungleTileThreshold     = 140;
	private const int SnowTileThreshold       = 1500;
	private const int DesertTileThreshold     = 1500;
	private const int MushroomTileThreshold   = 100;

	public static Biome GetForTile(int tileX, int tileY)
	{
		if (tileY > Main.UnderworldLayer) return Biome.Underworld;
		if (WorldGen.oceanDepths(tileX, tileY)) return Biome.Ocean;

		Scan(tileX, tileY,
			out int jungle, out int snow, out int sand,
			out int hallow, out int evil, out int blood,
			out int mushroom);

		if (mushroom >= MushroomTileThreshold)  return Biome.Mushroom;
		if (jungle   >= JungleTileThreshold)    return Biome.Jungle;
		if (snow     >= SnowTileThreshold)      return Biome.Snow;
		if (hallow   >= HallowTileThreshold)    return Biome.Hallow;
		if (blood    >= CrimsonTileThreshold)   return Biome.Crimson;
		if (evil     >= CorruptionTileThreshold) return Biome.Corruption;
		if (sand     >= DesertTileThreshold)    return Biome.Desert;
		return Biome.Forest;
	}

	// Public for any future consumer with custom threshold logic.
	public static void Scan(int cx, int cy,
		out int jungle, out int snow, out int sand,
		out int hallow, out int evil, out int blood,
		out int mushroom)
	{
		jungle = snow = sand = hallow = evil = blood = mushroom = 0;

		int half = Main.buffScanAreaWidth / 2;
		int vhalf = Main.buffScanAreaHeight / 2;
		var area = WorldUtils.ClampToWorld(
			new Rectangle(cx - half, cy - vhalf, Main.buffScanAreaWidth, Main.buffScanAreaHeight));

		int tileTypeCount = TileLoader.TileCount;
		var counts = new int[tileTypeCount];

		for (int i = area.Left; i < area.Right; i++)
		{
			for (int j = area.Top; j < area.Bottom; j++)
			{
				var tile = Main.tile[i, j];
				if (tile == null || !tile.HasTile) continue;
				if (TileID.Sets.isDesertBiomeSand[tile.TileType] && WorldGen.oceanDepths(i, j))
					continue;
				counts[tile.TileType]++;
			}
		}

		bool remix = Main.remixWorld;
		for (int t = 0; t < tileTypeCount; t++)
		{
			int n = counts[t];
			if (n == 0) continue;
			hallow   += n * TileID.Sets.HallowBiome[t];
			snow     += n * TileID.Sets.SnowBiome[t];
			mushroom += n * TileID.Sets.MushroomBiome[t];
			sand     += n * TileID.Sets.SandBiome[t];
			evil     += n * (remix ? TileID.Sets.RemixCorruptBiome[t] : TileID.Sets.CorruptBiome[t]);
			blood    += n * (remix ? TileID.Sets.RemixCrimsonBiome[t] : TileID.Sets.CrimsonBiome[t]);
			jungle   += n * (remix ? TileID.Sets.RemixJungleBiome[t]  : TileID.Sets.JungleBiome[t]);
		}

		int holyOriginal = hallow;
		hallow -= evil;
		hallow -= blood;
		evil   -= holyOriginal;
		blood  -= holyOriginal;
		if (hallow < 0) hallow = 0;
		if (evil   < 0) evil   = 0;
		if (blood  < 0) blood  = 0;
	}
}
