#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Steam;
using Terraria.Localization;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Verbatim port of SteamParallelMultiblockMachine.addDisplayText (java:131-162).
// Steam multis have no EU container - recipe EU is paid as steam from the
// bound SteamHatchPartMachine via SteamEnergyRecipeHandler.
public static class SteamParallelMultiblockLayout
{
	private const int Padding = 12;
	private const int TitleH  = 14;
	private const int BodyW   = 280;
	private const int BodyH   = 14 * 12;

	public static MachineUILayout Build(SteamParallelMultiblockMachine machine)
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

	private static List<string> BuildDisplayLines(SteamParallelMultiblockMachine machine)
	{
		var lines = new List<string>();
		var recipeLogic = machine.Recipe;

		// DEVIATION: prepended matcher error when
		// unformed (upstream emits nothing). Consistent with Generic / EBF / etc.
		if (!machine.IsFormed)
			lines.Add(Machine.RecipeStatusText.StatusLineForMulti(machine, recipeLogic));

		// Verbatim java:133 - IDisplayUIMachine.super.addDisplayText (no-op today).
		foreach (var part in machine.GetParts())
			part.AddMultiText(lines);

		if (machine.IsFormed)
		{
			// Only emit steam-stored once a hatch is bound.
			long capacity = machine.SteamCapacity;
			if (capacity > 0)
			{
				lines.Add(Language.GetTextValue("gtceu.multiblock.steam.steam_stored",
					machine.SteamStored.ToString("N0"), capacity.ToString("N0")));
			}

			if (!recipeLogic.IsWorkingEnabled())
			{
				lines.Add(Language.GetTextValue("gtceu.multiblock.work_paused"));
			}
			else if (recipeLogic.IsActive())
			{
				lines.Add(Language.GetTextValue("gtceu.multiblock.running"));
				if (machine.MaxParallels > 1)
					lines.Add(Language.GetTextValue("gtceu.multiblock.parallel",
						machine.MaxParallels.ToString("N0")));
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
				lines.Add(Language.GetTextValue("gtceu.multiblock.steam.low_steam"));
		}

		return lines;
	}
}
