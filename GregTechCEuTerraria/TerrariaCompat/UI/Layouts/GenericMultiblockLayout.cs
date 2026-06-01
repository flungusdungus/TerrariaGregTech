#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Machine.Multiblock;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Verbatim port of WEMM.addDisplayText + IDisplayUIMachine.super.addDisplayText
// part-walk. Drives every electric-multi panel (Std / Coil / EBF). One
// UIMultiLineDynamicLabel rebuilds the line list per frame. Mode-selector
// satellite is attached universally by MachineUIState.AppendModeSelectPanel.
public static class GenericMultiblockLayout
{
	private const int Padding = 12;
	private const int TitleH  = 14;
	private const int BodyW   = 280;
	private const int BodyH   = 14 * 14; // up to ~14 lines

	public static MachineUILayout Build(WorkableElectricMultiblockMachine machine)
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

	// Verbatim port of WEMM.addDisplayText(textList).
	internal static List<string> BuildDisplayLines(WorkableElectricMultiblockMachine machine)
	{
		var lines = new List<string>();
		var recipeLogic = machine.Recipe;

		int numParallels, subtickParallels, batchParallels, totalRuns;
		bool exact = false;
		var lastRecipe = recipeLogic.GetLastRecipe();
		if (recipeLogic.IsActive() && lastRecipe != null)
		{
			numParallels      = lastRecipe.Parallels;
			subtickParallels  = lastRecipe.SubtickParallels;
			batchParallels    = lastRecipe.BatchParallels;
			totalRuns         = lastRecipe.GetTotalRuns();
			exact = true;
		}
		else
		{
			numParallels      = machine.GetParallelHatch()?.CurrentParallel ?? 0;
			subtickParallels  = 0;
			batchParallels    = 0;
			totalRuns         = 0;
		}

		// DEVIATION - prepend the matcher's
		// state.Error.ErrorInfo on line 1 so the player sees WHY a structure
		// won't form without alt-tabbing to the world-hover tooltip. Upstream
		// emits only the two-line `invalid_structure` block.
		if (!machine.IsFormed)
			lines.Add(RecipeStatusText.StatusLineForMulti(machine, recipeLogic));

		// DEVIATION: honour the recipe's
		// `hide_duration` flag to swap to percent-only progress. Upstream's
		// research_station overrides addDisplayText for the same effect because
		// duration_is_total_cwu recipes use ticks-as-CWU-budget and seconds are
		// meaningless. MP client fallback: GetLastRecipe is null pre-resolve, so
		// re-resolve from the synced LastRecipeId.
		var activeRecipe = recipeLogic.GetLastRecipe();
		if (activeRecipe == null && machine.LastRecipeId is { } rid)
			activeRecipe = machine.GetRecipeType()?.GetRecipeById(rid);
		bool hideDuration = activeRecipe != null &&
			global::GregTechCEuTerraria.Api.Recipe.RecipeDataUtil.GetBool(activeRecipe.Data, "hide_duration");

		var b = MultiblockDisplayText.Create(lines, machine.IsFormed)
			// machine.IsActive (polymorphic) - recipeLogic.IsActive() reads
			// false forever on multis that override IsRecipeLogicAvailable()
			// (LargeMiner/FluidDrillingRig run their own OnTick loop).
			.SetWorkingStatus(recipeLogic.IsWorkingEnabled(), machine.DisplayActive)
			.AddEnergyUsageLine(machine.GetDisplayEnergyContainer())
			.AddEnergyTierLine(machine.MultiTier)
			.AddMachineModeLine(machine.GetRecipeType(), machine.GetRecipeTypes().Length > 1)
			.AddTotalRunsLine(totalRuns)
			.AddParallelsLine(numParallels, exact)
			.AddSubtickParallelsLine(subtickParallels)
			.AddBatchModeLine(machine.IsBatchEnabled(), batchParallels)
			.AddWorkingStatusLine();

		if (hideDuration)
			b.AddProgressLineOnlyPercent(recipeLogic.GetProgressPercent());
		else
			b.AddProgressLine(recipeLogic);

		b.AddRecipeFailReasonLine(recipeLogic)
			.AddOutputLines(lastRecipe);

		// TerrariaCompat aid (no upstream parallel): formed-but-idle, list the
		// current bus/hatch contents so the player can see why a recipe won't match.
		if (machine.IsFormed && !machine.DisplayActive)
			MultiblockInputDisplay.Append(machine, lines);

		// Verbatim WEMM.java:136 - getDefinition().getAdditionalDisplay().accept(...).
		// Coil-multi defs set this via CoilAdditionalDisplay lambdas.
		machine.Definition?.AdditionalDisplay?.Invoke(machine, lines);

		// Verbatim WEMM.java:137 - IDisplayUIMachine.super.addDisplayText. The
		// part walk is structurally present but currently no-op (no upstream
		// IMultiPart.addMultiText override today).
		foreach (var part in machine.GetParts())
			part.AddMultiText(lines);

		return lines;
	}
}
