#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Data.Chemical.Material;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Tools;

// Material -> Terraria-progression tier (0=ULV -> 9=UHV post-ML) + per-tier
// stat anchor on vanilla pick/damage/useTime breakpoints. Upstream HL is too
// compressed (HL 3 = steel..diamond), so Overrides fan the dense buckets out.
public static class ToolTier
{
	public const int TierCount = 10;

	// Default HL -> tier (indexed by HL): HL1 flint/wood->ULV, HL2 iron->MV,
	// HL3 steel->HV (fanned by overrides), HL4 ->IV, HL5 ->ZPM, HL6 ->UHV+.
	private static readonly int[] HLToTier = { 0, 0, 2, 3, 5, 7, 9, 9 };

	// Per-material remap to each material's boss-drop-table tier; unlisted
	// materials fall through to HLToTier.
	private static readonly Dictionary<string, int> Overrides = new(StringComparer.Ordinal)
	{
		// Steam
		["bronze"]           = 0,
		["invar"]            = 0,
		// LV
		["iron"]             = 1,
		["wrought_iron"]     = 1,
		["rose_gold"]        = 1,
		["steel"]            = 1,
		// MV
		["aluminium"]        = 2,
		["cobalt_brass"]     = 2,
		["sterling_silver"]  = 2,
		// HV
		["stainless_steel"]  = 3,
		["damascus_steel"]   = 3,
		["blue_steel"]       = 3,
		["red_steel"]        = 3,
		["diamond"]          = 3,
		// EV
		["vanadium_steel"]   = 4,
		["titanium"]         = 4,
		// IV
		["tungsten_carbide"] = 5,
		["tungsten_steel"]   = 5,
		["netherite"]        = 5,
		// LuV
		["hsse"]             = 6,
		["ultimet"]          = 6,
		// ZPM
		["duranium"]         = 7,
		// UV
		["naquadah_alloy"]   = 8,
		// UHV+
		["neutronium"]       = 9,
	};

	public static int For(Material m)
	{
		if (Overrides.TryGetValue(m.Id, out int t)) return t;
		int hl = m.Tool?.HarvestLevel ?? 0;
		return HLToTier[Math.Clamp(hl, 0, HLToTier.Length - 1)];
	}

	// Stat anchors, set ~10-20% above the vanilla peer at each tier (the GT grind
	// is steeper, so payoff should beat the equivalent vanilla tool). Per-tier
	// vanilla comparison is in each row's trailing comment.
	public readonly record struct Anchor(int Pick, int Axe, int Hammer, int Damage, int UseTime);

	private static readonly Anchor[] Anchors =
	{
		new( 40,  40,  35,  10, 23), // 0 ULV   - copper-tier   (vanilla pick 35, dmg 7,  t 23)
		new( 60,  55,  45,  16, 21), // 1 LV    - iron-tier     (vanilla 50, 10, 22)
		new( 80,  70,  55,  22, 19), // 2 MV    - silver/gold   (vanilla 59, 15, 21)
		new(130,  85,  70,  40, 17), // 3 HV    - molten/cobalt (vanilla 100/110, ~16, 20)
		new(230, 100,  85,  65, 15), // 4 EV    - adamantite/chloro range (post-Plantera in our boss map: needs Pickaxe-Axe 200 power)
		new(245, 110,  95,  90, 13), // 5 IV    - chloro / picksaw (vanilla 200/210)
		new(250, 120, 105, 115, 11), // 6 LuV   - spectre / solar (vanilla 210/225)
		new(255, 125, 115, 145,  9), // 7 ZPM   - luminite range  (vanilla 225, ~105, ~11)
		new(260, 125, 125, 185,  7), // 8 UV    - luminite + post-ML headroom
		new(265, 130, 140, 230,  5), // 9 UHV+  - Macrocosm (beats Zenith dmg 190 @ useTime 7)
	};

	public static Anchor AnchorFor(int tier) =>
		Anchors[Math.Clamp(tier, 0, Anchors.Length - 1)];

	// Blend weight toward the tier anchor (0 = pure upstream formula, 1 = anchor
	// only). 0.8 keeps the anchor dominant (so Neutronium > Luminite/Zenith) while
	// the 20% upstream weight preserves per-material variation. High weight is
	// needed because upstream's pickPower (28 + HL*20) saturates below luminite 225.
	public const float AnchorBlend = 0.8f;

	public static int Blend(int upstream, int anchor) =>
		(int)Math.Round(upstream * (1f - AnchorBlend) + anchor * AnchorBlend);
}
