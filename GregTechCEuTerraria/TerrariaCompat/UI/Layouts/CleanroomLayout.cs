#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Electric;
using Terraria.Localization;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Verbatim port of CleanroomMachine.addDisplayText (java:451-500).
// DEVIATIONS:
//   - dimensions line uses 2D variant (`.1.2d` LxH only - no F axis).
//   - invalid_structure hover tooltip dropped (no per-line hover).
//   - Prepended actionable matcher error when unformed.
public static class CleanroomLayout
{
	private const int Padding = 12;
	private const int TitleH  = 14;
	private const int BodyW   = 280;
	private const int BodyH   = 14 * 12;

	public static MachineUILayout Build(CleanroomMachine machine)
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

		return layout;
	}

	private static List<string> BuildDisplayLines(CleanroomMachine machine)
	{
		var lines = new List<string>();
		var recipeLogic = machine.Recipe;

		// Prepended actionable matcher error (DEVIATION).
		if (!machine.IsFormed)
			lines.Add(RecipeStatusText.StatusLineForMulti(machine, recipeLogic));

		// Upstream cleanroom doesn't call IDisplayUIMachine.super.addDisplayText
		// (verified) - no part-walk here.

		if (machine.IsFormed)
		{
			long maxVoltage = machine.GetMaxVoltage();
			if (maxVoltage > 0)
			{
				int voltageTier = VoltageTiers.FloorTierByVoltage(maxVoltage);
				string voltageName = VoltageTiers.ShortName((VoltageTier)voltageTier);
				lines.Add(Language.GetTextValue("gtceu.multiblock.max_energy_per_tick",
					maxVoltage.ToString("N0"), voltageName));
			}

			var cleanroomType = machine.CleanroomTypeResolved;
			if (cleanroomType != null)
				lines.Add(Language.GetTextValue(cleanroomType.TranslationKey));

			if (!recipeLogic.IsWorkingEnabled())
			{
				lines.Add(Language.GetTextValue("gtceu.multiblock.work_paused"));
			}
			else if (recipeLogic.IsActive())
			{
				lines.Add(Language.GetTextValue("gtceu.multiblock.running"));
				int currentProgress = (int)(recipeLogic.GetProgressPercent() * 100);
				double maxInSec     = recipeLogic.GetMaxProgress() / 20.0;
				double currentInSec = recipeLogic.GetProgress()    / 20.0;
				lines.Add(Language.GetTextValue("gtceu.multiblock.progress",
					currentInSec.ToString("0.00"), maxInSec.ToString("0.00"),
					currentProgress));
			}
			else
			{
				lines.Add(Language.GetTextValue("gtceu.multiblock.idling"));
			}

			if (recipeLogic.IsWaiting())
				lines.Add(Language.GetTextValue("gtceu.multiblock.waiting"));

			lines.Add(Language.GetTextValue(machine.CleanroomActive
				? "gtceu.multiblock.cleanroom.clean_state"
				: "gtceu.multiblock.cleanroom.dirty_state"));
			lines.Add(Language.GetTextValue("gtceu.multiblock.cleanroom.clean_amount",
				machine.CleanAmount));

			lines.Add(Language.GetTextValue("gtceu.multiblock.dimensions.0"));
			lines.Add(Language.GetTextValue("gtceu.multiblock.dimensions.1.2d",
				machine.FormedTileWidth / 2, machine.FormedTileHeight / 2));
		}
		else
		{
			lines.Add(Language.GetTextValue("gtceu.multiblock.invalid_structure"));
		}

		return lines;
	}
}
