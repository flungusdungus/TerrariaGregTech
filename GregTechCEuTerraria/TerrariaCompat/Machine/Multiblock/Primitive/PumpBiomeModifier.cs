#nullable enable
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Primitive;

// Adapted port of GTUtil.getPumpBiomeModifier. Returns mB-per-cycle water
// production for a primitive_pump at world-tile (x, y). Upstream biome-tag
// query -> server-safe tile scan via BiomeProbe (TileID.Sets.*Biome[] +
// vanilla buff-scan thresholds). tML's SceneMetrics has no Zone* flags, so
// we scan ourselves.
//
// Biome -> mB/cycle: Underworld=-1, Ocean=1000, Mushroom=800, Jungle=350,
// Snow=300, Hallow=250, Corruption/Crimson=175, Desert=170, default=100.
// Order of checks matters when zones overlap (mirrors upstream).
public static class PumpBiomeModifier
{
	public const int BUCKET_VOLUME = 1000;

	// Underworld -1 = no water (sentinel for the pump's lazy-init gate).
	public static int GetForTile(int tileX, int tileY) => BiomeProbe.GetForTile(tileX, tileY) switch
	{
		BiomeProbe.Biome.Underworld => -1,
		BiomeProbe.Biome.Ocean      => BUCKET_VOLUME,
		BiomeProbe.Biome.Mushroom   => BUCKET_VOLUME * 4 / 5,
		BiomeProbe.Biome.Jungle     => BUCKET_VOLUME * 35 / 100,
		BiomeProbe.Biome.Snow       => BUCKET_VOLUME * 3 / 10,
		BiomeProbe.Biome.Hallow     => BUCKET_VOLUME / 4,
		BiomeProbe.Biome.Corruption => BUCKET_VOLUME * 175 / 1000,
		BiomeProbe.Biome.Crimson    => BUCKET_VOLUME * 175 / 1000,
		BiomeProbe.Biome.Desert     => BUCKET_VOLUME * 170 / 1000,
		_                           => BUCKET_VOLUME / 10,
	};
}
