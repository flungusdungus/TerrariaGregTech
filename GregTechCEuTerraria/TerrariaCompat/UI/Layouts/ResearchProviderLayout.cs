#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Machine.Multiblock;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Electric.Research;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Port of the passive-provider `addDisplayText` on HPCAMachine /
// DataBankMachine / NetworkSwitchMachine (HPCAMachine.java:285,
// DataBankMachine.java:178, NetworkSwitchMachine.java:118). These override
// getProgress() to 0 and show a two-state "Idling" / "Providing" panel with NO
// progress line - generic_multi would mis-render them as busy recipe machines.
// Per-controller energy/computation lines come from AdditionalDisplay; the
// two-state status line is appended LAST (upstream call order).
public static class ResearchProviderLayout
{
	private const int Padding = 12;
	private const int TitleH  = 14;
	private const int BodyW   = 280;
	private const int BodyH   = 14 * 12; // up to ~12 lines

	public static MachineUILayout Build(WorkableElectricMultiblockMachine machine)
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

	internal static List<string> BuildDisplayLines(WorkableElectricMultiblockMachine machine)
	{
		var lines = new List<string>();
		var recipeLogic = machine.Recipe;

		// Unformed: surface the matcher's actionable error.
		if (!machine.IsFormed)
			lines.Add(RecipeStatusText.StatusLineForMulti(machine, recipeLogic));

		machine.Definition?.AdditionalDisplay?.Invoke(machine, lines);

		// `providing` flag per-controller, verbatim with each addDisplayText:
		// HPCA is Idling until a Research Station requests CWU; DataBank/Switch
		// provide constantly while formed + powered + not paused.
		bool providing = machine is HPCAMachine hpca
			? hpca.DisplayCachedCWUt > 0
			: machine.DisplayActive && recipeLogic.IsWorkingEnabled();

		MultiblockDisplayText.Create(lines, machine.IsFormed)
			.SetWorkingStatus(true, providing)
			.SetWorkingStatusKeys(
				"gtceu.multiblock.idling",
				"gtceu.multiblock.idling",
				"gtceu.multiblock.data_bank.providing")
			.AddWorkingStatusLine();

		return lines;
	}
}
