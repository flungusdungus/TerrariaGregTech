#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Common.Energy;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Pre-registered TooltipBuilders for machines with runtime-formatted tooltip
// lines (%s / %d placeholders in upstream's lang). MachineRegistry.Register
// auto-attaches by id. Values verbatim from upstream Java constants; see
// per-entry comments for source.
public static class MachineTooltipBuilders
{
	// MachineTooltipLookup converts upstream's %s / %d to {0} etc. before passing in.
	private static readonly Dictionary<string, Action<List<string>, MachineDefinition>> _byId = new()
	{
		// DataAccessHatchMachine. Line 1: "Adds %s slots for Data Items".
		["data_access_hatch"] = static (lines, def) =>
		{
			int slots = def.PartIo != null
				? SlotsForTier(def)
				: 1;
			if (lines.Count > 1) lines[1] = string.Format(lines[1], slots);
		},

		// DataBankMachine.EUT_PER_HATCH = VA[EV], EUT_PER_HATCH_CHAINED = VA[LuV].
		["data_bank"] = static (lines, _) =>
		{
			int eutNormal  = VoltageTiers.VA((int)VoltageTier.EV);
			int eutChained = VoltageTiers.VA((int)VoltageTier.LuV);
			if (lines.Count > 3) lines[3] = string.Format(lines[3], eutNormal);
			if (lines.Count > 4) lines[4] = string.Format(lines[4], eutChained);
		},

		// NetworkSwitchMachine.EUT_PER_HATCH = VA[IV].
		["network_switch"] = static (lines, _) =>
		{
			int eut = VoltageTiers.VA((int)VoltageTier.IV);
			if (lines.Count > 3) lines[3] = string.Format(lines[3], eut);
		},

		// PowerSubstationMachine.MAX_BATTERY_LAYERS = 18,
		// PASSIVE_DRAIN_MAX_PER_STORAGE = 100_000 (kEU/t = / 1000 = 100).
		["power_substation"] = static (lines, _) =>
		{
			const int maxLayers = 18;
			const int kEutPerStorage = 100;
			if (lines.Count > 2) lines[2] = string.Format(lines[2], maxLayers);
			if (lines.Count > 4) lines[4] = string.Format(lines[4], kEutPerStorage);
		},

		// Upstream IMiner.getWorkingArea(tier * 8) = tier * 16 - 1.
		["miner"] = static (lines, def) =>
		{
			int tier = (def.Tiers.Length > 0 ? (int)def.Tiers[0] : (int)VoltageTier.LV);
			int area = tier * 16 - 1;
			if (lines.Count > 0) lines[0] = string.Format(lines[0], area, area);
		},
	};

	// Verbatim DataAccessHatchMachine.GetInventorySize().
	private static int SlotsForTier(MachineDefinition def)
	{
		int tier = def.Tiers.Length > 0 ? (int)def.Tiers[0] : (int)VoltageTier.HV;
		return tier switch
		{
			(int)VoltageTier.LuV => 16,
			(int)VoltageTier.EV  => 9,
			(int)VoltageTier.HV  => 4,
			_                           => 1,
		};
	}

	public static Action<List<string>, MachineDefinition>? Get(string? id) =>
		id != null && _byId.TryGetValue(id, out var b) ? b : null;
}
