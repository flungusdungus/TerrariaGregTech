#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Electric.Research;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Port of ResearchStationMachine.addDisplayText (a CUSTOM override, NOT the
// generic WEMM body) - hence its own layout rather than generic_multi:
//   .setWorkingStatus(recipeLogic.isWorkingEnabled(), recipeLogic.isActive())
//   .setWorkingStatusKeys("idling", "work_paused", "research_station.researching")
//   .addEnergyUsageLine(energyContainer)
//   .addEnergyTierLine(tier)
//   .addWorkingStatusLine()
//   .addProgressLineOnlyPercent(recipeLogic.getProgressPercent())
// No parallels / machine-mode / fail-reason / output lines (upstream's research
// addDisplayText has none). WAITING counts as active -> shows "Researching" while
// under-powered, exactly like upstream (an under-powered station is only visible
// as stuck 0% there - which is why the CWU line below was approved).
public static class ResearchStationLayout
{
	private const int Padding = 12;
	private const int TitleH  = 14;
	private const int BodyW   = 320;
	private const int BodyH   = 14 * 10;

	public static MachineUILayout Build(ResearchStationMachine machine)
	{
		var layout = new MachineUILayout
		{
			Width  = Padding + BodyW + Padding,
			Height = Padding + TitleH + BodyH + Padding,
			Title  = machine.DisplayName,
		};
		layout.Widgets.Add(new MultiLineDynamicLabelWidgetSpec(
			X: Padding, Y: Padding + TitleH,
			Getter: () => BuildDisplayLines(machine)));
		return layout;
	}

	internal static List<string> BuildDisplayLines(ResearchStationMachine machine)
	{
		var lines = new List<string>();
		var recipeLogic = machine.Recipe;

		// Unformed: actionable matcher error (same TerrariaCompat first-line
		// deviation generic_multi uses) so the player sees WHY it won't form.
		if (!machine.IsFormed)
			lines.Add(RecipeStatusText.StatusLineForMulti(machine, recipeLogic));

		// Verbatim upstream ResearchStationMachine.addDisplayText.
		Api.Machine.Multiblock.MultiblockDisplayText.Create(lines, machine.IsFormed)
			.SetWorkingStatus(recipeLogic.IsWorkingEnabled(), machine.DisplayActive)
			.SetWorkingStatusKeys(
				"gtceu.multiblock.idling",
				"gtceu.multiblock.work_paused",
				"gtceu.multiblock.research_station.researching")
			.AddEnergyUsageLine(machine.GetDisplayEnergyContainer())
			.AddEnergyTierLine(machine.MultiTier)
			.AddWorkingStatusLine()
			.AddProgressLineOnlyPercent(recipeLogic.GetProgressPercent());

		// DEVIATION: upstream's addComputationUsage
		// ExactLine is commented out (TODO). Surfaced + enhanced - capacity vs the
		// active/blocked recipe's required CWU/t, red + an "insufficient" line when
		// the HPCA chain can't meet it. Both values are read-only server snapshots
		// (GetMaxCWUt + non-mutating item match) synced to MP clients.
		int capacity = machine.DisplayCapacityCwu;
		int req      = machine.DisplayRequiredCwu;
		if (req > 0)
		{
			string color = capacity < req ? "FF5555" : "55FFFF";
			lines.Add($"Computation: [c/{color}:{capacity} / {req} CWU/t]");
			if (capacity < req)
				lines.Add($"[c/FF5555:Insufficient computation - need {req} CWU/t, HPCA chain provides {capacity}]");
		}
		else if (capacity > 0)
		{
			lines.Add($"Computation: [c/55FFFF:{capacity} CWU/t available]");
		}

		return lines;
	}
}
