#nullable enable
using System;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;
using Terraria;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

// Terraria-aware tiered fish loot for FisherMachine (upstream rolls MC's
// BuiltInLootTables.FISHING / FISHING_FISH, absent in Terraria).
//
// The rarity math is a verbatim port of Terraria's own fishing pipeline:
// luck-scales-power (Projectile.cs:19264) + FishingCheck_RollDropLevels
// (Projectile.cs:20103). Hand-rolled only: (a) synthetic tier-derived power/luck
// SOURCE (no rod), and (b) the fish/crate item PICK (vanilla's Main.FishDropsDB
// is player+bobber-coupled, uncallable). Biome keys off the water tile below.
// junkEnabled true = full pool, false = fish only.
public static class FishingLootRoller
{
	private enum Rarity { Junk, Common, Uncommon, Rare, VeryRare, Legendary, Crate }

	// Synthetic tier-derived fishing power (no rod), scaled onto vanilla's ~0..200 range.
	public static int FishingPower(VoltageTier tier) => tier switch
	{
		VoltageTier.LV  => 30,
		VoltageTier.MV  => 55,
		VoltageTier.HV  => 85,
		VoltageTier.EV  => 120,
		VoltageTier.IV  => 160,
		VoltageTier.LuV => 210,
		_               => 30,
	};

	// Synthetic tier-derived luck (no player -> MP-correct), scaled onto vanilla's ~-1..+1.
	public static float SyntheticLuck(VoltageTier tier) => tier switch
	{
		VoltageTier.LV  => 0.00f,
		VoltageTier.MV  => 0.10f,
		VoltageTier.HV  => 0.20f,
		VoltageTier.EV  => 0.35f,
		VoltageTier.IV  => 0.50f,
		VoltageTier.LuV => 0.70f,
		_               => 0.00f,
	};

	// Roll one fishing result. waterTileX/Y is a water cell under the machine -
	// biome keys off it. Returns an Item to deposit, or IsAir when the roll
	// yields nothing.
	public static Item Roll(VoltageTier tier, int waterTileX, int waterTileY, bool junkEnabled)
	{
		// Power = synthetic tier power, luck applied as vanilla (Projectile.cs:19264-19274).
		int power = ApplyLuckToPower(FishingPower(tier), SyntheticLuck(tier));
		if (power < 1) power = 1;

		// Projectile.FishingCheck_RollDropLevels (Projectile.cs:20103).
		RollDropLevels(power, out bool common, out bool uncommon, out bool rare,
		                      out bool veryRare, out bool legendary, out bool crate);

		// Junk rolled separately (Projectile.cs:19323); junkEnabled=false suppresses it.
		bool junk = junkEnabled
		         && Main.rand.Next(50) > power
		         && Main.rand.Next(50) > power;

		var biome = BiomeProbe.GetForTile(waterTileX, waterTileY);

		// Priority pick (the one deviation - no Main.FishDropsDB): crate first,
		// then highest rarity down, then junk, then common-fish fallback.
		int itemId =
			crate     ? RollCrate(biome, waterTileY)                      :
			legendary ? RollLegendary(biome, Main.hardMode)               :
			veryRare  ? RollFish(biome, Rarity.VeryRare, waterTileY)      :
			rare      ? RollFish(biome, Rarity.Rare,     waterTileY)      :
			uncommon  ? RollFish(biome, Rarity.Uncommon, waterTileY)      :
			junk      ? RollJunk()                                        :
			            RollFish(biome, Rarity.Common,   waterTileY);

		if (itemId <= 0) return new Item();
		var item = new Item();
		item.SetDefaults(itemId);
		return item;
	}

	// Projectile.FishingCheck luck step (Projectile.cs:19264-19274): +luck chance
	// to x1.1..1.4, -luck chance to x0.6..0.9.
	private static int ApplyLuckToPower(int power, float luck)
	{
		if (luck < 0f)
		{
			if (Main.rand.NextFloat() < 0f - luck)
				power = (int)(power * (0.9 - Main.rand.NextFloat() * 0.3));
		}
		else if (Main.rand.NextFloat() < luck)
		{
			power = (int)(power * (1.1 + Main.rand.NextFloat() * 0.3));
		}
		return power;
	}

	// Projectile.FishingCheck_RollDropLevels (Projectile.cs:20103): six power-keyed
	// rolls. Omits the player cratePotion +15 bonus (no player) -> base crateChance 10.
	private static void RollDropLevels(int power, out bool common, out bool uncommon,
		out bool rare, out bool veryRare, out bool legendary, out bool crate)
	{
		int nCommon   = Math.Max(2, 150 / power);
		int nUncommon = Math.Max(3, 150 * 2 / power);
		int nRare     = Math.Max(4, 150 * 7 / power);
		int nVeryRare = Math.Max(5, 150 * 15 / power);
		int nLegend   = Math.Max(6, 150 * 30 / power);
		int crateChance = 10;

		common    = Main.rand.Next(nCommon)   == 0;
		uncommon  = Main.rand.Next(nUncommon) == 0;
		rare      = Main.rand.Next(nRare)     == 0;
		veryRare  = Main.rand.Next(nVeryRare) == 0;
		legendary = Main.rand.Next(nLegend)   == 0;
		crate     = Main.rand.Next(100) < crateChance;
	}

	private static int RollJunk()
	{
		short[] junk = { ItemID.OldShoe, ItemID.TinCan, ItemID.Seaweed };
		return junk[Main.rand.Next(junk.Length)];
	}

	// Crate selection: biome-specific crates first, else depth-weighted
	// wooden/iron/gold ladder; hard variants in HM (no Main.FishDropsDB to reuse).
	private static int RollCrate(BiomeProbe.Biome biome, int waterY)
	{
		bool hm = Main.hardMode;
		// Biome-specific common crates (vanilla types - verbatim ids).
		switch (biome)
		{
			case BiomeProbe.Biome.Jungle:
				return hm ? ItemID.JungleFishingCrateHard : ItemID.JungleFishingCrate;
			case BiomeProbe.Biome.Snow:
				return hm ? ItemID.FrozenCrateHard : ItemID.FrozenCrate;
			case BiomeProbe.Biome.Ocean:
				return hm ? ItemID.OceanCrateHard : ItemID.OceanCrate;
			case BiomeProbe.Biome.Hallow:
				return hm ? ItemID.HallowedFishingCrateHard : ItemID.HallowedFishingCrate;
			case BiomeProbe.Biome.Corruption:
				return hm ? ItemID.CorruptFishingCrateHard : ItemID.CorruptFishingCrate;
			case BiomeProbe.Biome.Crimson:
				return hm ? ItemID.CrimsonFishingCrateHard : ItemID.CrimsonFishingCrate;
			case BiomeProbe.Biome.Underworld:
				return hm ? ItemID.ObsidianLockbox : ItemID.HellstoneBar; // cheeky fallback - lava-water shouldn't reach here
		}
		bool underground = waterY > Main.worldSurface;
		int roll = Main.rand.Next(100);
		if (hm)
		{
			if (underground)
				return roll < 60 ? ItemID.WoodenCrateHard : roll < 90 ? ItemID.IronCrateHard : ItemID.GoldenCrateHard;
			return roll < 70 ? ItemID.WoodenCrateHard : roll < 95 ? ItemID.IronCrateHard : ItemID.GoldenCrateHard;
		}
		if (underground)
			return roll < 60 ? ItemID.WoodenCrate : roll < 90 ? ItemID.IronCrate : ItemID.GoldenCrate;
		return roll < 70 ? ItemID.WoodenCrate : roll < 95 ? ItemID.IronCrate : ItemID.GoldenCrate;
	}

	// Top-tier catch: ~30% golden crate, else a biome-signature trophy fish /
	// fishing weapon (HM weapons gated on hm, falling back to the biome's rarest fish).
	private static int RollLegendary(BiomeProbe.Biome biome, bool hm)
	{
		if (Main.rand.Next(100) < 30)
			return hm ? ItemID.GoldenCrateHard : ItemID.GoldenCrate;

		return biome switch
		{
			BiomeProbe.Biome.Hallow     => hm ? ItemID.CrystalSerpent : ItemID.Prismite,
			BiomeProbe.Biome.Corruption => hm ? ItemID.Toxikarp       : ItemID.Ebonkoi,
			BiomeProbe.Biome.Crimson    => hm ? ItemID.Bladetongue    : ItemID.Hemopiranha,
			BiomeProbe.Biome.Jungle     => ItemID.VariegatedLardfish,
			_                           => ItemID.ReaverShark,
		};
	}

	// Per-(biome, rarity) fish lookup (non-exhaustive, matched to vanilla pools).
	// Falls back to Bass when the requested rarity is empty for the biome.
	private static int RollFish(BiomeProbe.Biome biome, Rarity rarity, int waterY)
	{
		short[]? pool = (biome, rarity) switch
		{
			// === Forest (default surface) ===
			(BiomeProbe.Biome.Forest, Rarity.Common)   => new[] { ItemID.Bass, ItemID.Trout },
			(BiomeProbe.Biome.Forest, Rarity.Uncommon) => new[] { ItemID.NeonTetra, ItemID.Trout },
			(BiomeProbe.Biome.Forest, Rarity.Rare)     => new[] { ItemID.GoldenCarp, ItemID.RedSnapper },
			(BiomeProbe.Biome.Forest, Rarity.VeryRare) => new[] { ItemID.ReaverShark, ItemID.GoldenCarp },

			// === Snow ===
			(BiomeProbe.Biome.Snow, Rarity.Common)     => new[] { ItemID.AtlanticCod, ItemID.FrostMinnow },
			(BiomeProbe.Biome.Snow, Rarity.Uncommon)   => new[] { ItemID.Trout, ItemID.AtlanticCod },
			(BiomeProbe.Biome.Snow, Rarity.Rare)       => new[] { ItemID.Salmon, ItemID.RedSnapper },
			(BiomeProbe.Biome.Snow, Rarity.VeryRare)   => new[] { ItemID.ReaverShark, ItemID.Salmon },

			// === Jungle ===
			(BiomeProbe.Biome.Jungle, Rarity.Common)   => new[] { ItemID.Bass, ItemID.NeonTetra },
			(BiomeProbe.Biome.Jungle, Rarity.Uncommon) => new[] { ItemID.VariegatedLardfish, ItemID.NeonTetra },
			(BiomeProbe.Biome.Jungle, Rarity.Rare)     => new[] { ItemID.VariegatedLardfish, ItemID.GoldenCarp },
			(BiomeProbe.Biome.Jungle, Rarity.VeryRare) => new[] { ItemID.ReaverShark },

			// === Ocean ===
			(BiomeProbe.Biome.Ocean, Rarity.Common)    => new[] { ItemID.Damselfish, ItemID.Tuna, ItemID.AtlanticCod },
			(BiomeProbe.Biome.Ocean, Rarity.Uncommon)  => new[] { ItemID.Tuna, ItemID.RedSnapper },
			(BiomeProbe.Biome.Ocean, Rarity.Rare)      => new[] { ItemID.RedSnapper, ItemID.GoldenCarp },
			(BiomeProbe.Biome.Ocean, Rarity.VeryRare)  => new[] { ItemID.ReaverShark, ItemID.GoldenCarp },

			// === Hallow ===
			(BiomeProbe.Biome.Hallow, Rarity.Common)   => new[] { ItemID.PrincessFish, ItemID.Bass },
			(BiomeProbe.Biome.Hallow, Rarity.Uncommon) => new[] { ItemID.PrincessFish, ItemID.NeonTetra },
			(BiomeProbe.Biome.Hallow, Rarity.Rare)     => new[] { ItemID.Prismite, ItemID.PrincessFish },
			(BiomeProbe.Biome.Hallow, Rarity.VeryRare) => new[] { ItemID.ChaosFish, ItemID.Prismite },

			// === Corruption ===
			(BiomeProbe.Biome.Corruption, Rarity.Common)   => new[] { ItemID.DoubleCod, ItemID.Bass },
			(BiomeProbe.Biome.Corruption, Rarity.Uncommon) => new[] { ItemID.Ebonkoi, ItemID.DoubleCod },
			(BiomeProbe.Biome.Corruption, Rarity.Rare)     => new[] { ItemID.Ebonkoi, ItemID.Stinkfish },
			(BiomeProbe.Biome.Corruption, Rarity.VeryRare) => new[] { ItemID.Stinkfish, ItemID.RedSnapper },

			// === Crimson ===
			(BiomeProbe.Biome.Crimson, Rarity.Common)   => new[] { ItemID.DoubleCod, ItemID.Bass },
			(BiomeProbe.Biome.Crimson, Rarity.Uncommon) => new[] { ItemID.CrimsonTigerfish, ItemID.DoubleCod },
			(BiomeProbe.Biome.Crimson, Rarity.Rare)     => new[] { ItemID.CrimsonTigerfish, ItemID.Hemopiranha },
			(BiomeProbe.Biome.Crimson, Rarity.VeryRare) => new[] { ItemID.Hemopiranha, ItemID.RedSnapper },

			// === Mushroom ===
			(BiomeProbe.Biome.Mushroom, Rarity.Common)   => new[] { ItemID.Bass, ItemID.NeonTetra },
			(BiomeProbe.Biome.Mushroom, Rarity.Uncommon) => new[] { ItemID.GlowingMushroom, ItemID.NeonTetra },
			(BiomeProbe.Biome.Mushroom, Rarity.Rare)     => new[] { ItemID.GlowingMushroom, ItemID.SpecularFish },
			(BiomeProbe.Biome.Mushroom, Rarity.VeryRare) => new[] { ItemID.GoldenCarp },

			// === Desert ===
			(BiomeProbe.Biome.Desert, Rarity.Common)   => new[] { ItemID.Bass },
			(BiomeProbe.Biome.Desert, Rarity.Uncommon) => new[] { ItemID.NeonTetra, ItemID.Bass },
			(BiomeProbe.Biome.Desert, Rarity.Rare)     => new[] { ItemID.GoldenCarp },
			(BiomeProbe.Biome.Desert, Rarity.VeryRare) => new[] { ItemID.GoldenCarp, ItemID.ReaverShark },

			// === Underworld - a fisher with a lava-water mix; falls through to bass ===
			(BiomeProbe.Biome.Underworld, _)           => new[] { ItemID.Bass },

			// === Cavern (deep, no biome) ===
			(_, Rarity.Common)   => new[] { ItemID.SpecularFish, ItemID.Bass },
			(_, Rarity.Uncommon) => new[] { ItemID.NeonTetra, ItemID.SpecularFish },
			(_, Rarity.Rare)     => new[] { ItemID.GoldenCarp },
			(_, Rarity.VeryRare) => new[] { ItemID.ReaverShark },

			_ => null,
		};

		if (pool is null || pool.Length == 0) return ItemID.Bass;
		return pool[Main.rand.Next(pool.Length)];
	}
}
