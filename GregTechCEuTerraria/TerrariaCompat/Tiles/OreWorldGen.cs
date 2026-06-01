#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using GregTechCEuTerraria.TerrariaCompat.Worldgen;
using Terraria;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.WorldBuilding;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles;

// Vein-based ore generation. Iterates VeinRegistry definitions extracted from
// upstream GTOres.java. Each vein's MC Layer maps to a Terraria depth band:
//
//   STONE      -> upper cavern    (rockLayer .. midCavern)
//   DEEPSLATE  -> lower cavern    (midCavern .. UnderworldLayer)
//   NETHERRACK -> upper underworld (UnderworldLayer .. +60% of underworld span)
//   ENDSTONE   -> lower underworld (+60% .. maxTilesY-50)
//
// Per-vein placement count scales by upstream weight. Each placement drops a
// dense oval where stone is replaced by weighted-random vein materials.
public sealed class OreWorldGen : ModSystem
{
	public override void ModifyWorldGenTasks(List<GenPass> tasks, ref double totalWeight)
	{
		// Must run AFTER "Underworld" - that pass wipes the bottom slab and rebuilds
		// it as ash/hellstone/lava. Anchoring on "Shinies" (which runs BEFORE
		// "Underworld" in vanilla's worldgen config) caused every NETHERRACK /
		// ENDSTONE vein to be silently overwritten. Fall back to "Shinies" only if
		// "Underworld" is missing (custom worldgen setups).
		int idx = tasks.FindIndex(p => p.Name.Equals("Underworld"));
		if (idx < 0) idx = tasks.FindIndex(p => p.Name.Equals("Shinies"));
		if (idx < 0) return;
		tasks.Insert(idx + 1, new GregTechVeinPass("GregTech Veins", 200f));
	}
}

internal sealed class GregTechVeinPass : GenPass
{
	// Per-vein placement count = weight x WeightMultiplier x worldAreaRatio,
	// floored at MinPlacements. Tuned at small-world baseline (4200x1200).
	// Vanilla Terraria scales every ore band by Main.maxTilesX * Main.maxTilesY,
	// so we do the same - large worlds get proportionally more vein placements.
	private const float WeightMultiplier = 0.60f;
	private const int MinPlacements = 8;
	// Reference world area = small-world dimensions (4200 x 1200). All
	// per-vein placement counts are normalized to this baseline.
	private const float BaselineWorldArea = 4200f * 1200f;

	// Number of fresh random positions to try if a placement's center isn't a
	// replaceable tile (open cave / lava / unmoved chunk boundary).
	private const int PlacementRetries = 5;

	// Oval dimensions derive from clusterSize. clusterSize is upstream's
	// "vein size" - we treat it as approximate oval area, then translate to a
	// flat oval (rx ~ sqrtcs, ry ~ 0.55 x rx).
	private const float AspectRatio = 0.55f;

	// Multiplier on upstream clusterSize. Bumped above 1.0 to make each ore
	// patch visually chunkier without changing per-vein placement count.
	private const float ClusterSizeMultiplier = 3.0f;

	// Fill probability inside the oval - dense at center, feathered at edges.
	private const float FillCenter = 0.85f;
	private const float FillEdge = 0.35f;

	private static readonly HashSet<ushort> ReplaceableTiles = new()
	{
		TileID.Stone, TileID.Dirt, TileID.ClayBlock, TileID.Sand, TileID.Silt, TileID.Slush,
		TileID.Mud, TileID.SnowBlock, TileID.IceBlock, TileID.Sandstone,
		TileID.HardenedSand, TileID.Granite, TileID.Marble, TileID.Crimstone,
		TileID.Ebonstone, TileID.Pearlstone, TileID.Ash, TileID.Hellstone,
		// Grass variants - vanilla copper/tin TileRunner replaces these on hilltops
		// via overRide=true. Without them, every IsViablePlacement attempt at the
		// surface dirt-grass interface rejects and retries, so hilltop copper veins
		// effectively never land.
		TileID.Grass, TileID.JungleGrass, TileID.CorruptGrass, TileID.CrimsonGrass,
		TileID.HallowedGrass, TileID.MushroomGrass, TileID.CorruptJungleGrass, TileID.CrimsonJungleGrass,
	};

	public GregTechVeinPass(string name, float loadWeight) : base(name, loadWeight) { }

	protected override void ApplyPass(GenerationProgress progress, GameConfiguration configuration)
	{
		progress.Message = "GregTech veins";
		var veins = VeinRegistry.All;
		var log = ModContent.GetInstance<GregTechCEuTerraria>().Logger;
		log.Info($"[GT vein pass] starting: {veins.Count} vein defs, {OreTileRegistry.Count} ore tiles registered, " +
			$"world {Main.maxTilesX}x{Main.maxTilesY}, surfaceLow={(int)GenVars.worldSurfaceLow} rock={(int)Main.rockLayer} underworld={Main.UnderworldLayer}");
		if (veins.Count == 0) { log.Warn("[GT vein pass] no veins to place"); return; }

		float worldAreaRatio = (Main.maxTilesX * (float)Main.maxTilesY) / BaselineWorldArea;
		log.Info($"[GT vein pass] worldAreaRatio={worldAreaRatio:F2} (baseline 4200x1200)");

		var rand = WorldGen.genRand;
		int totalTilesPlaced = 0;
		int totalPlacements = 0;
		int skippedNoTiles = 0;
		int skippedBandTiny = 0;
		// Per-layer + per-vein breakdown so we can diagnose "no ores in band X".
		var perLayerTiles = new Dictionary<string, int>();
		var perLayerPlacementsAttempted = new Dictionary<string, int>();
		var perLayerPlacementsSucceeded = new Dictionary<string, int>();

		for (int vi = 0; vi < veins.Count; vi++)
		{
			var vein = veins[vi];

			var tileChoices = vein.Materials
				.Select(vm => (Tile: OreTileRegistry.Get(vm.MaterialId), vm.Weight))
				.Where(p => p.Tile.HasValue)
				.Select(p => (TileType: p.Tile!.Value, p.Weight))
				.ToList();

			if (tileChoices.Count == 0) { skippedNoTiles++; continue; }
			int totalWeight = tileChoices.Sum(c => c.Weight);
			if (totalWeight <= 0) { skippedNoTiles++; continue; }

			var dims = new WorldDimensions(
				SurfaceLow: (int)GenVars.worldSurfaceLow,
				SurfaceHigh: (int)GenVars.worldSurfaceHigh,
				RockLayer: (int)Main.rockLayer,
				UnderworldLayer: Main.UnderworldLayer,
				MaxY: Main.maxTilesY);
			// Per-vein window: each vein's MC heightMin/heightMax is remapped into
			// its layer's Terraria band so e.g. diamond stays near bedrock while
			// lapis spans the upper deepslate. See LayerDepthMapping.ForVein.
			(int yMin, int yMax) = LayerDepthMapping.ForVein(
				vein.Layer, vein.HeightMin, vein.HeightMax, dims);
			if (yMax - yMin < 20) { skippedBandTiny++; continue; }

			int placements = Math.Max(MinPlacements, (int)(vein.Weight * WeightMultiplier * worldAreaRatio));
			int veinTilesPlaced = 0;
			int veinPlacementsSucceeded = 0;

			for (int k = 0; k < placements; k++)
			{
				int clusterSize = (int)(rand.Next(vein.ClusterSizeMin, vein.ClusterSizeMax + 1) * ClusterSizeMultiplier);
				int rx = (int)MathF.Round(MathF.Sqrt(clusterSize));
				int ry = (int)MathF.Round(rx * AspectRatio);
				if (rx < 3) rx = 3;
				if (ry < 2) ry = 2;

				int tilesPlaced = 0;
				// Retry if center is in open air / lava - common in upper bands.
				for (int attempt = 0; attempt < PlacementRetries; attempt++)
				{
					int cx = rand.Next(50, Main.maxTilesX - 50);
					int cy = rand.Next(yMin, yMax);
					if (!IsViablePlacement(cx, cy)) continue;
					tilesPlaced = PlaceWeightedOvalVein(cx, cy, rx, ry, tileChoices, totalWeight, vein.Density);
					if (tilesPlaced > 0) break;
				}
				totalTilesPlaced += tilesPlaced;
				veinTilesPlaced += tilesPlaced;
				if (tilesPlaced > 0) veinPlacementsSucceeded++;
				totalPlacements++;
			}

			perLayerTiles.TryGetValue(vein.Layer, out int lt); perLayerTiles[vein.Layer] = lt + veinTilesPlaced;
			perLayerPlacementsAttempted.TryGetValue(vein.Layer, out int la); perLayerPlacementsAttempted[vein.Layer] = la + placements;
			perLayerPlacementsSucceeded.TryGetValue(vein.Layer, out int ls); perLayerPlacementsSucceeded[vein.Layer] = ls + veinPlacementsSucceeded;
			log.Info($"[GT vein pass]   {vein.Id} ({vein.Layer}) y[{yMin}..{yMax}] mc[{vein.HeightMin}..{vein.HeightMax}] " +
				$"placements={veinPlacementsSucceeded}/{placements} tiles={veinTilesPlaced}");

			progress.Set((float)(vi + 1) / veins.Count);
		}

		log.Info($"[GT vein pass] done: {totalPlacements} vein placements, {totalTilesPlaced} ore tiles placed. " +
			$"Skipped: {skippedNoTiles} (no registered tiles), {skippedBandTiny} (band too narrow).");
		foreach (var layer in new[] { "STONE", "DEEPSLATE", "NETHERRACK", "ENDSTONE" })
		{
			int t = perLayerTiles.GetValueOrDefault(layer);
			int la = perLayerPlacementsAttempted.GetValueOrDefault(layer);
			int ls = perLayerPlacementsSucceeded.GetValueOrDefault(layer);
			log.Info($"[GT vein pass] {layer,-10} placements={ls}/{la} tiles={t}");
		}
	}

	private static bool IsViablePlacement(int cx, int cy)
	{
		if (cx < 1 || cx >= Main.maxTilesX - 1 || cy < 1 || cy >= Main.maxTilesY - 1)
			return false;
		var t = Main.tile[cx, cy];
		return t.HasTile && ReplaceableTiles.Contains(t.TileType);
	}

	private static int PlaceWeightedOvalVein(int cx, int cy, int radiusX, int radiusY,
		List<(ushort TileType, int Weight)> choices, int totalWeight, float baseDensity)
	{
		var rand = WorldGen.genRand;
		// Floor at 0.7 - most upstream veins set density in [0.1, 0.3] which
		// would otherwise scale every cell to <0.5 and produce visibly scattered
		// patches. Tuned for "chunky oval, edges still feathered" - not bricks.
		float densityScale = MathF.Max(0.7f, baseDensity * 1.5f);
		int placed = 0;
		int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;

		for (int dy = -radiusY; dy <= radiusY; dy++)
		{
			for (int dx = -radiusX; dx <= radiusX; dx++)
			{
				float nx = (float)dx / radiusX;
				float ny = (float)dy / radiusY;
				float d2 = nx * nx + ny * ny;
				if (d2 > 1f) continue;

				float dist = MathF.Sqrt(d2);
				float p = (FillEdge + (FillCenter - FillEdge) * (1f - dist)) * densityScale;
				if (rand.NextFloat() > p) continue;

				int x = cx + dx;
				int y = cy + dy;
				if (x < 1 || x >= Main.maxTilesX - 1 || y < 1 || y >= Main.maxTilesY - 1) continue;

				var tile = Main.tile[x, y];
				if (!tile.HasTile) continue;
				if (!ReplaceableTiles.Contains(tile.TileType)) continue;

				ushort pickedType = WeightedPick(choices, totalWeight, rand);
				// ResetToType is the canonical mutation API - it both sets the
				// type and clears slope/halfbrick flags that might otherwise
				// confuse rendering after a type change.
				tile.ResetToType(pickedType);
				placed++;
				if (x < minX) minX = x;
				if (x > maxX) maxX = x;
				if (y < minY) minY = y;
				if (y > maxY) maxY = y;
			}
		}

		// CRITICAL: re-frame the touched region. Without this, replaced tiles
		// render the blank top-left (0,0) frame of their sheet -> invisible ore.
		if (placed > 0)
		{
			for (int y = minY - 1; y <= maxY + 1; y++)
				for (int x = minX - 1; x <= maxX + 1; x++)
					WorldGen.TileFrame(x, y);
		}
		return placed;
	}

	private static ushort WeightedPick(List<(ushort TileType, int Weight)> choices, int totalWeight, Terraria.Utilities.UnifiedRandom rand)
	{
		int roll = rand.Next(totalWeight);
		int acc = 0;
		foreach (var c in choices)
		{
			acc += c.Weight;
			if (roll < acc) return c.TileType;
		}
		return choices[^1].TileType;
	}
}
