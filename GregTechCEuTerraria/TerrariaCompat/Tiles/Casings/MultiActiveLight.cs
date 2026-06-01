#nullable enable
using GregTechCEuTerraria.Api.Block;
using Microsoft.Xna.Framework;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Casings;

// Per-tile glow intensity for a casing while its multi is FORMED + WORKING (the
// in-ActiveCasingState gate lives in CasingTile.ModifyLight). Coils glow tier-
// scaled hot-orange, fireboxes a flat orange, everything else dark - upstream
// baked these as texture emissives; in 2D we lift them into world light instead.
internal static class MultiActiveLight
{
	// Hot-orange base (~torch hue, scaled down so per-cell emission stacks with
	// the controller's half-torch without saturating neighbours).
	private static readonly Vector3 BaseGlow = new(0.55f, 0.34f, 0.13f);

	private static readonly Vector3 Firebox = BaseGlow;

	// Cupronickel (tier 0) ~70% of base, +12%/tier -> tritanium (7) ~155%.
	private static Vector3 CoilLight(int tier)
	{
		float scale = 0.70f + 0.12f * tier;
		return BaseGlow * scale;
	}

	public static Vector3 For(string casingId)
	{
		// Coil - name maps 1:1 to a CoilType registry entry.
		var coil = CoilType.GetByName(StripCoilSuffix(casingId));
		if (coil is not null) return CoilLight(coil.Tier);

		// Firebox casings - any "_firebox_casing" suffix.
		if (casingId.EndsWith("_firebox_casing")) return Firebox;

		return Vector3.Zero;
	}

	// Strip the `_coil_block` suffix to match `CoilType.Name`.
	private static string StripCoilSuffix(string id) =>
		id.EndsWith("_coil_block") ? id[..^"_coil_block".Length] : id;
}
