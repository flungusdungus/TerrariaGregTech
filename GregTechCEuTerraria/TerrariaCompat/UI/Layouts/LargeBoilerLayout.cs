#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Steam;
using GregTechCEuTerraria.TerrariaCompat.Net.Actions;
using Terraria.Localization;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Verbatim port of LargeBoilerMachine.addDisplayText (java:199-220).
// DEVIATIONS:
//   - throttle line hover tooltip dropped (no per-line hover support).
//   - [-]/[+] buttons split out as separate widgets (upstream inlines them).
//   - Prepended actionable matcher error when unformed.
public static class LargeBoilerLayout
{
	private const int Padding = 12;
	private const int TitleH  = 14;
	private const int BodyW   = 280;
	private const int BodyH   = 14 * 10;

	public static MachineUILayout Build(LargeBoilerMachine machine)
	{
		var layout = new MachineUILayout
		{
			Width  = Padding + BodyW + Padding,
			Height = Padding + TitleH + BodyH + Padding,
			Title  = machine.DisplayName,
		};
		int baseY = Padding + TitleH;

		layout.Widgets.Add(new MultiLineDynamicLabelWidgetSpec(
			X: Padding, Y: baseY,
			Getter: () => BuildDisplayLines(machine)));

		// Throttle [-] / [+] - separate widgets pinned right of the
		// "Adjust Throttle:" label (4th dynamic line at Y=50 tile units).
		const int BtnY = 50;
		const int BtnX1 = 68;
		const int BtnX2 = 88;
		// Hidden while unformed - the matching label row is also gated.
		layout.Widgets.Add(new TextButtonWidgetSpec(X: BtnX1, Y: BtnY,
			Label: () => "[-]", Width: 16, Height: 12,
			OnLeft: () => MachineActions.Send(new BoilerThrottleSetAction(machine.Throttle - 5), machine),
			Tooltip: null,
			Visible: () => machine.IsFormed));
		layout.Widgets.Add(new TextButtonWidgetSpec(X: BtnX2, Y: BtnY,
			Label: () => "[+]", Width: 16, Height: 12,
			OnLeft: () => MachineActions.Send(new BoilerThrottleSetAction(machine.Throttle + 5), machine),
			Tooltip: null,
			Visible: () => machine.IsFormed));

		return layout;
	}

	private static List<string> BuildDisplayLines(LargeBoilerMachine machine)
	{
		var lines = new List<string>();
		var recipeLogic = machine.Recipe;

		if (!machine.IsFormed)
			lines.Add(RecipeStatusText.StatusLineForMulti(machine, recipeLogic, workingVerb: "Burning"));

		// Verbatim LargeBoilerMachine.java:200 - super.addDisplayText.
		foreach (var part in machine.GetParts())
			part.AddMultiText(lines);

		if (machine.IsFormed)
		{
			// Storage is degC offset, display is kelvins (+274 verbatim upstream).
			lines.Add(Language.GetTextValue("gtceu.multiblock.large_boiler.temperature",
				(machine.CurrentTemperature + 274).ToString("N0"),
				(machine.MaxTemperature     + 274).ToString("N0")));

			const int TICKS_PER_STEAM_GENERATION = 5;
			lines.Add(Language.GetTextValue("gtceu.multiblock.large_boiler.steam_output",
				(machine.SteamGenerated / TICKS_PER_STEAM_GENERATION).ToString("N0")));

			// Color inlined in the value so the locale template stays single-arg.
			lines.Add(Language.GetTextValue("gtceu.multiblock.large_boiler.throttle",
				$"[c/55FFFF:{machine.Throttle}%]"));

			// Label only - buttons are separate widgets (see header).
			lines.Add(Language.GetTextValue("gtceu.multiblock.large_boiler.throttle_modify"));
		}

		return lines;
	}
}
