#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Common.Energy;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Armor;

public enum ArmorSuite { Nano, Quark }
public enum ArmorPiece { Helmet, Chest, Legs }

// One GregTech power-armor piece. EU constants verbatim upstream (GTItems.java);
// defense values Terraria-anchored (see GTArmorItem header). VanillaEquipSlot
// points at a head/body/leg index so the piece draws from the player's install.
public sealed class ArmorSpec
{
	public string Id { get; init; } = "";               // upstream bare id (no gtceu:)
	public string Label { get; init; } = "";
	public ArmorSuite Suite { get; init; }
	public ArmorPiece Piece { get; init; }
	public VoltageTier Tier { get; init; }
	public int EnergyPerUse { get; init; }              // upstream energyPerUse
	public long Capacity { get; init; }                 // upstream maxCapacity
	public int FullDefense { get; init; }               // charged
	public int FloorDefense { get; init; }              // drained (~20%)
	public int VanillaEquipSlot { get; init; }          // headSlot/bodySlot/legSlot index
	public int Rarity { get; init; }
	public bool HasFlight { get; init; }                // the Advanced chestplate (jetpack)
}

public static class ArmorCatalog
{
	// Upstream EU constants (GTItems.java, default config): Nano HV 512/6.4M,
	// Quark IV 8192/100M.
	private const int  NanoEu  = 512;
	private const long NanoCap = 6_400_000L;
	private const int  QuarkEu  = 8192;
	private const long QuarkCap = 100_000_000L;

	public static readonly IReadOnlyList<ArmorSpec> All = new[]
	{
		new ArmorSpec { Id = "nanomuscle_helmet",     Label = "NanoMuscle™ Suite Helmet",     Suite = ArmorSuite.Nano,  Piece = ArmorPiece.Helmet, Tier = VoltageTier.HV, EnergyPerUse = NanoEu,  Capacity = NanoCap,  FullDefense = 10, FloorDefense = 2, VanillaEquipSlot = 41,  Rarity = ItemRarityID.LightPurple },
		new ArmorSpec { Id = "nanomuscle_chestplate", Label = "NanoMuscle™ Suite Chestplate", Suite = ArmorSuite.Nano,  Piece = ArmorPiece.Chest,  Tier = VoltageTier.HV, EnergyPerUse = NanoEu,  Capacity = NanoCap,  FullDefense = 20, FloorDefense = 4, VanillaEquipSlot = 24,  Rarity = ItemRarityID.LightPurple },
		new ArmorSpec { Id = "nanomuscle_leggings",   Label = "NanoMuscle™ Suite Leggings",   Suite = ArmorSuite.Nano,  Piece = ArmorPiece.Legs,   Tier = VoltageTier.HV, EnergyPerUse = NanoEu,  Capacity = NanoCap,  FullDefense = 14, FloorDefense = 3, VanillaEquipSlot = 23,  Rarity = ItemRarityID.LightPurple },

		new ArmorSpec { Id = "quarktech_helmet",      Label = "QuarkTech™ Suite Helmet",      Suite = ArmorSuite.Quark, Piece = ArmorPiece.Helmet, Tier = VoltageTier.IV, EnergyPerUse = QuarkEu, Capacity = QuarkCap, FullDefense = 14, FloorDefense = 3, VanillaEquipSlot = 157, Rarity = ItemRarityID.Yellow },
		new ArmorSpec { Id = "quarktech_chestplate",  Label = "QuarkTech™ Suite Chestplate",  Suite = ArmorSuite.Quark, Piece = ArmorPiece.Chest,  Tier = VoltageTier.IV, EnergyPerUse = QuarkEu, Capacity = QuarkCap, FullDefense = 32, FloorDefense = 6, VanillaEquipSlot = 106, Rarity = ItemRarityID.Yellow },
		new ArmorSpec { Id = "quarktech_leggings",    Label = "QuarkTech™ Suite Leggings",    Suite = ArmorSuite.Quark, Piece = ArmorPiece.Legs,   Tier = VoltageTier.IV, EnergyPerUse = QuarkEu, Capacity = QuarkCap, FullDefense = 18, FloorDefense = 4, VanillaEquipSlot = 98,  Rarity = ItemRarityID.Yellow },

		// Advanced chestplates - upstream's AdvancedNanoMuscleSuite /
		// AdvancedQuarkTechSuite. Same defense/passives as base; differentiator
		// is JETPACK FLIGHT (HasFlight) while charged.
		new ArmorSpec { Id = "advanced_nanomuscle_chestplate", Label = "Advanced NanoMuscle™ Suite Chestplate", Suite = ArmorSuite.Nano,  Piece = ArmorPiece.Chest, Tier = VoltageTier.HV, EnergyPerUse = NanoEu,  Capacity = NanoCap,  FullDefense = 20, FloorDefense = 4, VanillaEquipSlot = 24,  Rarity = ItemRarityID.Pink,    HasFlight = true },
		new ArmorSpec { Id = "advanced_quarktech_chestplate",  Label = "Advanced QuarkTech™ Suite Chestplate",  Suite = ArmorSuite.Quark, Piece = ArmorPiece.Chest, Tier = VoltageTier.IV, EnergyPerUse = QuarkEu, Capacity = QuarkCap, FullDefense = 32, FloorDefense = 6, VanillaEquipSlot = 106, Rarity = ItemRarityID.Cyan,    HasFlight = true },
	};
}
