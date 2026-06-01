#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Items;
using GregTechCEuTerraria.TerrariaCompat.Items.Registry;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.BossDrops;

// Tier-keyed boss-drop table. The per-tier shape:
// each tier row carries 3 distinct materials (Hull/Frame
// collapsed since they're always equal, plus Cable + Wire) and an optional
// component list (the SMD/wafer pieces that go INTO the tier's circuit).
//
// Per-material form picks raw_<m> if it exists upstream, else <m>_dust
// (alloys + non-mineable elements like graphene / americium have no raw).
// All ids resolve against the registry dump - items the dump doesn't carry
// are skipped with a warning so a missing entry never NRPs a boss kill.
public static class BossDropRegistry
{
	public readonly record struct Drop(int ItemType, int Min, int Max);
	public readonly record struct TierSpec(string Name, string[] Materials, string[]? Components);

	// Quantities - LV-baseline ranges, scaled by TierMultiplier[tierIdx].
	// Raw ore drops in bulk because players need a stockpile to feed the
	// ore-processing chain (macerator -> wash -> centrifuge -> smelt). Higher
	// tiers drop dramatically more - UV gives ~10x LV - both because the
	// bosses are harder and because UV-tier recipes consume many more
	// materials. First material in each tier row is the hull/frame metal
	// and gets a 2x range vs. cable/wire. All quantities clamp to the
	// Terraria stack ceiling (9999).
	private const int RawHullMin     =  16, RawHullMax     = 1024;
	private const int RawCableMin    =  16, RawCableMax    =  512;
	private const int DustHullMin    =  16, DustHullMax    =  256;
	private const int DustCableMin   =  16, DustCableMax   =  128;
	private const int ComponentMin   =   4, ComponentMax   =   16;
	private const int StackCeiling   = 9999;

	// Indexed by tier (0=Steam unused - King Slime is hand-authored, 1=LV..8=UV).
	// Roughly 1.4x per tier - LV anchor at 1.0, UV ~ 10x.
	private static readonly double[] TierMultiplier =
		{ 1.0, 1.0, 1.5, 2.0, 3.0, 4.0, 6.0, 8.0, 10.0 };

	// First entry per tier = Hull/Frame material (wider drop range).
	private static readonly TierSpec[] Tiers =
	{
		new("Steam", new[] { "bronze", "invar" },                                              null),
		new("LV",    new[] { "steel", "tin", "copper" },                                       new[] { "vacuum_tube", "resistor", "diode" }),
		new("MV",    new[] { "aluminium", "copper", "cupronickel" },                           new[] { "resistor", "transistor", "capacitor" }),
		new("HV",    new[] { "stainless_steel", "silver", "electrum" },                        new[] { "smd_diode", "smd_resistor", "smd_transistor" }),
		new("EV",    new[] { "titanium", "aluminium", "kanthal" },                             new[] { "smd_capacitor", "smd_inductor" }),
		new("IV",    new[] { "tungsten_steel", "tungsten", "graphene" },                       new[] { "advanced_smd_diode", "advanced_smd_resistor", "ram_wafer" }),
		new("LuV",   new[] { "hsss", "niobium_titanium", "ruridit" },                          new[] { "advanced_smd_transistor", "advanced_smd_capacitor", "nor_memory_chip" }),
		new("ZPM",   new[] { "osmiridium", "vanadium_gallium", "europium" },                   new[] { "advanced_smd_inductor", "nand_memory_chip", "cpu_wafer" }),
		new("UV",    new[] { "tritanium", "yttrium_barium_cuprate", "americium" },             new[] { "nano_cpu_wafer", "hpic_wafer" }),
	};

	// Hand-authored special: King Slime is the only Steam boss, so it drops a
	// heap of bronze + the prerequisite metals to bootstrap into Steam tier.
	private static readonly (string item, int min, int max)[] KingSlimeOverride =
	{
		// Bronze is the steam-tier hull alloy - needs the most. Scaled to roughly
		// match LV hull dust range so the player can bootstrap a full steam
		// workshop from one kill.
		("bronze_dust",  64, 512),
		("invar_dust",   16, 128),
		("copper_dust",  64, 512),
		("tin_dust",     64, 512),
		// Raw tin in LV-hull bulk so smelting + alloying chains have material.
		("raw_tin",      32, 256),
	};

	// NPCID -> (tier index, include circuit components). Components fire only
	// for the boss rows where the spec wrote "+ TIER circuit components".
	private static readonly (short Npc, int TierIdx, bool Components)[] BossTable =
	{
		// Steam (index 0) - King Slime gets the hand-authored override below.
		(NPCID.KingSlime,         0, false),

		// LV (index 1)
		(NPCID.EyeofCthulhu,      1, true),
		(NPCID.EaterofWorldsHead, 1, true),
		(NPCID.BrainofCthulhu,    1, true),
		(NPCID.Deerclops,         1, false),

		// MV (index 2)
		(NPCID.QueenBee,          2, false),
		(NPCID.SkeletronHead,     2, true),
		(NPCID.WallofFlesh,       2, true),

		// HV (index 3)
		(NPCID.PirateShip,        3, false), // Flying Dutchman
		(NPCID.TheDestroyer,      3, true),
		(NPCID.Retinazer,         3, true),
		(NPCID.Spazmatism,        3, true),
		(NPCID.SkeletronPrime,    3, true),

		// EV (index 4)
		(NPCID.QueenSlimeBoss,    4, false),
		(NPCID.Plantera,          4, true),

		// IV (index 5)
		(NPCID.MourningWood,      5, false),
		(NPCID.Everscream,        5, false),
		(NPCID.Pumpking,          5, true),
		(NPCID.SantaNK1,          5, true),
		(NPCID.IceQueen,          5, true),

		// LuV (index 6)
		(NPCID.Golem,             6, false),
		(NPCID.MartianSaucerCore, 6, true),

		// ZPM (index 7)
		(NPCID.DukeFishron,       7, false),
		(NPCID.CultistBoss,       7, true),

		// UV (index 8) - all four Pillars share the same drop (per design call).
		(NPCID.LunarTowerSolar,    8, false),
		(NPCID.LunarTowerVortex,   8, false),
		(NPCID.LunarTowerNebula,   8, false),
		(NPCID.LunarTowerStardust, 8, false),
		(NPCID.HallowBoss,         8, true), // Empress of Light
		(NPCID.MoonLordCore,       8, true),
	};

	// Resolved at Mod.Load - NPCID -> list of (itemType, min, max).
	private static readonly Dictionary<short, List<Drop>> _resolved = new();
	// NPCID -> list of multiblock-bag itemTypes assigned to this boss's tier.
	private static readonly Dictionary<short, List<int>> _bagsByBoss = new();

	// Per-tier resolved material/component lists, kept after Resolve() so a
	// custom ModNPC boss can mirror a vanilla boss's tier loot (e.g. the
	// Fallen EBF reuses Eye-of-Cthulhu's LV-tier "age loot"). Null until load.
	private static List<Drop>[]? _tierResolved;
	private static List<Drop>?[]? _tierComponents;

	public static bool TryGet(short npcType, out List<Drop> drops) =>
		_resolved.TryGetValue(npcType, out drops!);

	// Returns a fresh copy of the resolved drops for a tier index
	// (0=Steam..8=UV). `withComponents` mirrors the BossTable "include circuit
	// components" flag. Empty before Mod.Load finishes resolving.
	public static List<Drop> GetTierDrops(int tierIdx, bool withComponents)
	{
		var list = new List<Drop>();
		if (_tierResolved is null || tierIdx < 0 || tierIdx >= _tierResolved.Length) return list;
		list.AddRange(_tierResolved[tierIdx]);
		if (withComponents && _tierComponents is not null && _tierComponents[tierIdx] is { } comps)
			list.AddRange(comps);
		return list;
	}

	public static bool TryGetBags(short npcType, out List<int> bagItemTypes) =>
		_bagsByBoss.TryGetValue(npcType, out bagItemTypes!);

	// Map a tier index (0=Steam..8=UV) back to the bosses in BossTable at that
	// tier - used by the multiblock-bag wiring to attach each bag to every
	// boss in its tier bucket.
	public static IEnumerable<short> BossesForTier(int tierIdx)
	{
		foreach (var (npc, idx, _) in BossTable)
			if (idx == tierIdx) yield return npc;
	}

	public static void Resolve(Mod mod)
	{
		_resolved.Clear();
		int totalDrops = 0, missing = 0;

		Language.GetOrRegister("Mods.GregTechCEuTerraria.BossDrops.ConditionDescription",
			() => "Requires boss drops enabled in config.");
		Language.GetOrRegister("Mods.GregTechCEuTerraria.Configs.GTConfig.DisplayName", () => "GregTech");
		Language.GetOrRegister("Mods.GregTechCEuTerraria.Configs.GTConfig.EnableBossDrops.Label", () => "Enable GregTech boss drops");
		Language.GetOrRegister("Mods.GregTechCEuTerraria.Configs.GTConfig.EnableBossDrops.Tooltip",
			() => "If enabled, vanilla bosses drop tier-appropriate GregTech raw ores, dusts, and circuit components.");

		// Resolve each tier row's material + component item ids once, scaled
		// by the tier multiplier.
		var tierResolved = new List<Drop>[Tiers.Length];
		var tierComponents = new List<Drop>?[Tiers.Length];
		for (int i = 0; i < Tiers.Length; i++)
		{
			var t = Tiers[i];
			double mult = TierMultiplier[i];
			var mats = new List<Drop>();
			for (int j = 0; j < t.Materials.Length; j++)
			{
				bool isHull = j == 0;
				if (TryResolveMaterial(t.Materials[j], out int itemType, out bool isRaw))
				{
					int baseMin = isRaw ? (isHull ? RawHullMin  : RawCableMin)
					                    : (isHull ? DustHullMin : DustCableMin);
					int baseMax = isRaw ? (isHull ? RawHullMax  : RawCableMax)
					                    : (isHull ? DustHullMax : DustCableMax);
					mats.Add(new Drop(itemType, Scale(baseMin, mult), Scale(baseMax, mult)));
				}
				else
					{ mod.Logger.Warn($"[BossDrops] Tier {t.Name}: no item for material '{t.Materials[j]}' (tried raw_{t.Materials[j]} + {t.Materials[j]}_dust)"); missing++; }
			}
			tierResolved[i] = mats;

			if (t.Components is not null)
			{
				var comps = new List<Drop>();
				foreach (var c in t.Components)
				{
					if (TryResolveBareId(c, out int itemType))
						comps.Add(new Drop(itemType, Scale(ComponentMin, mult), Scale(ComponentMax, mult)));
					else
						{ mod.Logger.Warn($"[BossDrops] Tier {t.Name}: component '{c}' not found in registry dump"); missing++; }
				}
				tierComponents[i] = comps;
			}
		}

		// Keep the per-tier lists around for GetTierDrops (custom-boss reuse).
		_tierResolved = tierResolved;
		_tierComponents = tierComponents;

		// Compose per-boss drop lists.
		foreach (var (npc, tierIdx, withComponents) in BossTable)
		{
			if (npc == NPCID.KingSlime) continue; // handled below
			var list = new List<Drop>(tierResolved[tierIdx]);
			if (withComponents && tierComponents[tierIdx] is { } comps)
				list.AddRange(comps);
			_resolved[npc] = list;
			totalDrops += list.Count;
		}

		// King Slime hand-authored override.
		var ks = new List<Drop>();
		foreach (var (id, min, max) in KingSlimeOverride)
		{
			if (TryResolveBareId(id, out int t)) ks.Add(new Drop(t, min, max));
			else { mod.Logger.Warn($"[BossDrops] King Slime override: '{id}' not found"); missing++; }
		}
		_resolved[NPCID.KingSlime] = ks;
		totalDrops += ks.Count;

		mod.Logger.Info($"[BossDrops] Resolved {_resolved.Count} bosses, {totalDrops} drop entries" +
			(missing > 0 ? $" ({missing} unresolved - logged above)" : ""));

		ResolveMultiblockBags(mod);
	}

	// Walk every registered multiblock bag, look up its target tier, and add it
	// to every boss at that tier. Runs AFTER MultiblockBagLoader.Register so the
	// bag item types exist. Bags whose target tier has no boss assignment are
	// silently skipped (e.g. a future "post-Moon-Lord" tier 9 with no bosses).
	private static void ResolveMultiblockBags(Mod mod)
	{
		_bagsByBoss.Clear();
		int totalLinks = 0;
		foreach (var kv in MultiblockBag.MultiblockBagLoader.All)
		{
			int tierIdx = MultiblockBag.MultiblockBagTierMap.GetTier(kv.Key);
			foreach (var npc in BossesForTier(tierIdx))
			{
				if (!_bagsByBoss.TryGetValue(npc, out var list))
					_bagsByBoss[npc] = list = new List<int>();
				list.Add(kv.Value);
				totalLinks++;
			}
		}
		int bagCount = System.Linq.Enumerable.Count(MultiblockBag.MultiblockBagLoader.All);
		mod.Logger.Info($"[BossDrops] Linked {bagCount} multiblock bags to bosses ({totalLinks} (bag, boss) links).");
	}

	public static void Unload()
	{
		_resolved.Clear();
		_bagsByBoss.Clear();
		_tierResolved = null;
		_tierComponents = null;
	}

	private static int Scale(int baseValue, double mult)
	{
		int v = (int)System.Math.Round(baseValue * mult);
		return v > StackCeiling ? StackCeiling : (v < 1 ? 1 : v);
	}

	// Material picker: raw_<m> if it exists upstream, else <m>_dust. Mirrors
	// the spec's "ore for raws, dust for alloys" rule via dump existence.
	// `isRaw` reports which branch hit so quantity tiers can differ.
	private static bool TryResolveMaterial(string material, out int itemType, out bool isRaw)
	{
		if (MaterialItemRegistry.TryGetByUpstreamId("raw_" + material, out itemType)) { isRaw = true;  return true; }
		if (MaterialItemRegistry.TryGetByUpstreamId(material + "_dust", out itemType)) { isRaw = false; return true; }
		itemType = 0;
		isRaw = false;
		return false;
	}

	private static bool TryResolveBareId(string bareId, out int itemType)
	{
		// Material items first (raw_tin / copper_dust / bronze_dust ...),
		// then inert registry items (resistor / vacuum_tube / smd_* / wafers).
		if (MaterialItemRegistry.TryGetByUpstreamId(bareId, out itemType)) return true;
		if (RegistryItemLoader.TryGet("gtceu:" + bareId, out itemType)) return true;
		itemType = 0;
		return false;
	}
}
