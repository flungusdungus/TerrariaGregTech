#nullable enable
using System;

namespace GregTechCEuTerraria.TerrariaCompat.Worldgen;

// Pure, dependency-free mapping from upstream WorldGenLayers names to
// Terraria depth bands. Extracted from OreWorldGen so it's unit-testable
// without launching tML.
// SurfaceHigh = GenVars.worldSurfaceHigh = the DEEPEST surface y (top of the
// dirt-only zone, where vanilla copper's surface band ends and its dirt-band
// begins). Used as the top of the GT cavern zone so STONE-layer veins land in
// dirt + upper rock, matching vanilla copper's reachable depth.
public readonly record struct WorldDimensions(int SurfaceLow, int SurfaceHigh, int RockLayer, int UnderworldLayer, int MaxY);

// MC reference y-range per layer - used to project each vein's heightMin/heightMax
// from MC coords into its Terraria band. Values are the empirical envelope of
// upstream GTOres definitions (see `Data/Veins/*.json`).
internal readonly record struct LayerReferenceRange(int McMin, int McMax);

public static class LayerDepthMapping
{
	// Returns the inclusive-min, exclusive-max Y range a vein should generate in.
	// The GT "cavern" for layer-mapping purposes spans SurfaceLow..UnderworldLayer
	// - STARTING at SurfaceLow (= shallowest surface y = where vanilla tin/copper's
	// FIRST band begins) so GT tier-1 ores can appear at grass-level on hills,
	// matching the depth profile of vanilla tin/copper exactly. Dirt/Mud/etc. are
	// in ReplaceableTiles so dirt/surface placement works as well as cavern.
	//   STONE      -> upper cavern    (SurfaceLow .. midCavern)
	//   DEEPSLATE  -> lower cavern    (midCavern .. UnderworldLayer)
	//   NETHERRACK -> upper underworld (UnderworldLayer .. +60% of underworld span)
	//   ENDSTONE   -> lower underworld (+60% .. maxY-50)
	//   <unknown>  -> entire cavern-to-underworld range (safe fallback)
	public static (int yMin, int yMax) For(string layer, WorldDimensions dims)
	{
		int cavernTop = dims.SurfaceLow;
		int cavernSpan = dims.UnderworldLayer - cavernTop;
		int upperCavernEnd = cavernTop + cavernSpan / 2;

		int underworldBottom = dims.MaxY - 50;
		int underworldSpan = underworldBottom - dims.UnderworldLayer;
		int upperUnderworldEnd = dims.UnderworldLayer + underworldSpan * 60 / 100;

		return layer switch
		{
			"STONE"      => (cavernTop, upperCavernEnd),
			"DEEPSLATE"  => (upperCavernEnd, dims.UnderworldLayer),
			"NETHERRACK" => (dims.UnderworldLayer, upperUnderworldEnd),
			"ENDSTONE"   => (upperUnderworldEnd, underworldBottom),
			_            => (dims.SurfaceLow, dims.UnderworldLayer),
		};
	}

	// MC y-axis: HIGHER y = SHALLOWER. Terraria y-axis: LOWER y = SHALLOWER.
	// Envelopes are the empirical min/max of vein heightMin/heightMax in
	// Data/Veins/*.json - STONE is the widest because copper_tin_vein / coal_vein
	// span y -10..160; DEEPSLATE is the deepest because diamond/redstone_ow sit
	// near bedrock.
	private static LayerReferenceRange ReferenceRange(string layer) => layer switch
	{
		"STONE"      => new(-15, 160),
		"DEEPSLATE"  => new(-65,  10),
		"NETHERRACK" => new(  5, 120),
		"ENDSTONE"   => new(  5,  90),
		_            => new(-64, 256), // fallback: whole overworld stone column
	};

	// Returns the per-vein Y window inside its layer's band, linearly remapping
	// the vein's MC [heightMin..heightMax] from the layer's reference MC range.
	// MC high (shallow) -> Terraria yMin; MC low (deep) -> Terraria yMax.
	// Window is clamped to the layer band and widened to a minimum of `minSpan`
	// so a vein with a single-point MC range still has somewhere to land.
	public static (int yMin, int yMax) ForVein(
		string layer, int veinMcMin, int veinMcMax, WorldDimensions dims, int minSpan = 20)
	{
		var (bandMin, bandMax) = For(layer, dims);
		int bandSpan = bandMax - bandMin;
		if (bandSpan <= minSpan) return (bandMin, bandMax);

		var refRange = ReferenceRange(layer);
		int refSpan = refRange.McMax - refRange.McMin;
		if (refSpan <= 0) return (bandMin, bandMax);

		// MC high y -> small t (shallow); MC low y -> large t (deep).
		float TMap(int mcY)
		{
			float u = (float)(refRange.McMax - mcY) / refSpan;
			if (u < 0f) u = 0f; else if (u > 1f) u = 1f;
			return u;
		}

		int veinYMin = bandMin + (int)Math.Round(TMap(veinMcMax) * bandSpan);
		int veinYMax = bandMin + (int)Math.Round(TMap(veinMcMin) * bandSpan);

		if (veinYMin < bandMin) veinYMin = bandMin;
		if (veinYMax > bandMax) veinYMax = bandMax;
		if (veinYMax - veinYMin < minSpan)
		{
			int mid = (veinYMin + veinYMax) / 2;
			veinYMin = Math.Max(bandMin, mid - minSpan / 2);
			veinYMax = Math.Min(bandMax, veinYMin + minSpan);
			if (veinYMax > bandMax)
			{
				veinYMax = bandMax;
				veinYMin = Math.Max(bandMin, veinYMax - minSpan);
			}
		}
		return (veinYMin, veinYMax);
	}
}
