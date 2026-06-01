#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Electric.Research;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

// Port of HPCAMachine.addDisplayText / HPCAGridHandler.addInfo + Network
// SwitchMachine.addDisplayText's computation line, delivered via
// MachineDefinition.AdditionalDisplay (same path as the coil multis).
// APPROVED-STYLE DEVIATION: upstream uses gtceu.multiblock.hpca.* locale
// keys; we inline English + color tags until those keys are mirrored.
public static class HpcaAdditionalDisplay
{
	public static void HpcaInfo(MetaMachine controller, List<string> lines)
	{
		if (controller is not HPCAMachine h || !h.IsFormed) return;

		// Values via server-synced snapshot (handler is server-only).
		int maxCWUt = h.DisplayMaxCWUt;
		int demand  = h.DisplayCoolDemand;
		int avail   = h.DisplayCoolAvail;

		// Actionable diagnostics first. Power check runs even when IsActive
		// flickers true on an underpowered draining buffer; only "Idle" when
		// power is sustainable.
		if (maxCWUt == 0)
			lines.Add("[c/FF5555:No computation - add HPCA Computation Components to the grid]");
		else if (!ResearchPowerDiagnostics.Append(h, lines))
		{ /* underpowered line already appended */ }
		else if (h.DisplayCachedCWUt == 0)
			lines.Add("[c/AAAAAA:Idle - waiting for a Research Station to request computation]");
		if (maxCWUt > 0 && demand > avail)
			lines.Add("[c/FF5555:Insufficient cooling - components will overheat under load]");

		// Computation - cached this tick / max steady-state.
		lines.Add($"Computation: [c/55FFFF:{h.DisplayCachedCWUt} / {maxCWUt} CWU/t]");

		// Energy draw - cached this tick / max possible.
		lines.Add($"Energy: [c/AAAAAA:{h.DisplayCachedEUt:N0} / {h.DisplayMaxEUt:N0} EU/t]");

		// Full-load power check - "powered but no computation comes out" trap:
		// hatch can supply idle upkeep but not the ramp to DisplayMaxEUt.
		if (h.DisplayMaxInput > 0 && h.DisplayMaxEUt > h.DisplayMaxInput)
			lines.Add($"[c/FF5555:Energy Hatch too small for full load: needs up to {h.DisplayMaxEUt:N0} EU/t, hatch supplies {h.DisplayMaxInput:N0} - computation stalls under load]");

		// Cooling - demand vs available (red when under-cooled).
		string coolColor = avail < demand ? "FF5555" : "55FF55";
		lines.Add($"Cooling: [c/{coolColor}:{demand} demand / {avail} available]");

		// Bridging.
		lines.Add(h.DisplayHasBridge
			? "Bridging: [c/55FF55:enabled]"
			: "Bridging: [c/FF5555:disabled]");

		// Temperature (green < 500 / yellow < 750 / red).
		int temp = (int)System.Math.Round(h.Temperature);
		string tColor = temp < 500 ? "55FF55" : temp < 750 ? "FFFF55" : "FF5555";
		lines.Add($"Temperature: [c/{tColor}:{temp}degC]");
	}

	public static void DataBankInfo(MetaMachine controller, List<string> lines)
	{
		if (controller is not DataBankMachine db || !db.IsFormed) return;
		if (!ResearchPowerDiagnostics.Append(db, lines))
			return;
		lines.Add("[c/55FF55:Providing data access]");
	}

	public static void NetworkSwitchComputation(MetaMachine controller, List<string> lines)
	{
		if (controller is not NetworkSwitchMachine ns || !ns.IsFormed) return;
		if (!ResearchPowerDiagnostics.Append(ns, lines))
			return;
		int comp = ns.MaxComputationForDisplay;
		lines.Add($"Computation: [c/55FFFF:{comp} CWU/t]");
		if (comp == 0)
		{
			// Switch aggregates BRIDGE-CAPABLE sources only (verbatim
			// `if (!provider.canBridge(...)) continue;`). Common trap: HPCA
			// without Bridge Component -> skipped even with optical pipe linked.
			lines.Add("[c/FFAA44:No bridged computation source]");
			lines.Add("[c/FFAA44:- link a reception hatch via optical pipe to a bridging source]");
			lines.Add("[c/FFAA44:- the source HPCA needs an HPCA Bridge Component in its grid]");
		}
	}
}
