#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Common.Energy;
using Terraria.Localization;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

// Verbatim ports of upstream's coil-multi `additionalDisplay` lambdas
// (GTMultiMachines.java / GCYMMachines.java). Wired via
// MachineDefinition.AdditionalDisplay; the generic multi layout invokes after
// the standard builder pipeline.
public static class CoilAdditionalDisplay
{
	// EBF / mega_blast_furnace / alloy_blast_smelter. Upstream colors RED.
	public static void BlastFurnaceMaxTemperature(MetaMachine controller, List<string> lines)
	{
		if (controller is not CoilWorkableElectricMultiblockMachine c || !c.IsFormed) return;
		long heat = c.CoilType.Temperature
			+ 100L * Math.Max(0, c.MultiTier - (int)VoltageTier.MV);
		lines.Add(Language.GetTextValue(
			"gtceu.multiblock.blast_furnace.max_temperature",
			$"[c/FF5555:{heat:N0}K]"));
	}

	// pyrolyse_oven. Speed = coilTier == 0 ? 75 : 50 x (coilTier + 1).
	public static void PyrolyseOvenSpeed(MetaMachine controller, List<string> lines)
	{
		if (controller is not CoilWorkableElectricMultiblockMachine c || !c.IsFormed) return;
		int speed = c.CoilType.Tier == 0 ? 75 : 50 * (c.CoilType.Tier + 1);
		lines.Add(Language.GetTextValue(
			"gtceu.multiblock.pyrolyse_oven.speed", speed));
	}

	// multi_smelter: coil level + energy-discount multiplier.
	public static void MultiSmelterCoilStats(MetaMachine controller, List<string> lines)
	{
		if (controller is not CoilWorkableElectricMultiblockMachine c || !c.IsFormed) return;
		lines.Add(Language.GetTextValue(
			"gtceu.multiblock.multi_furnace.heating_coil_level",
			c.CoilType.Level));
		lines.Add(Language.GetTextValue(
			"gtceu.multiblock.multi_furnace.heating_coil_discount",
			c.CoilType.EnergyDiscount));
	}

	// cracker: energy = 100 - 10 x coilTier (% of base EU/t paid).
	public static void CrackingUnitEnergy(MetaMachine controller, List<string> lines)
	{
		if (controller is not CoilWorkableElectricMultiblockMachine c || !c.IsFormed) return;
		int energy = 100 - 10 * c.CoilType.Tier;
		lines.Add(Language.GetTextValue(
			"gtceu.multiblock.cracking_unit.energy", energy));
	}
}
