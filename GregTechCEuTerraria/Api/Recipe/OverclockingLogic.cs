#nullable enable
using System;
using GregTechCEuTerraria.Api.Machine.Feature;
using GregTechCEuTerraria.Api.Recipe.Content;
using GregTechCEuTerraria.Api.Recipe.Modifier;
using GregTechCEuTerraria.Common.Energy;

namespace GregTechCEuTerraria.Api.Recipe;

// Port of com.gregtechceu.gtceu.api.recipe.OverclockingLogic.
//
// Given a set of OCParams and a maxVoltage, produces an OCResult. Upstream is
// a @FunctionalInterface with static algorithms + default getModifier; C# uses
// a sealed class wrapping the runOverclockingLogic delegate.
//
// This file holds the instance / GetModifier / RecipeModifier-facing half; the
// pure overclock algorithms (StandardOC, SubTick*, HeatingCoilOC, ...) live in
// OverclockingLogic.Algorithms.cs (see that file's header - split so the
// pure-logic test project can compile them).
//
// Documented adaptations:
//   - getModifier's machine parameter is IRecipeLogicMachine, not MetaMachine
//     (see RecipeModifier - the Api/ layer cannot depend on TerrariaCompat/).
//   - GTMath.saturatedCast / IntMath.log2 collapsed to SaturatedCast /
//     Log2Floor helpers (in the Algorithms partial).
public sealed partial class OverclockingLogic
{
	private readonly Func<OCParams, long, OCResult> _run;

	private OverclockingLogic(Func<OCParams, long, OCResult> run) => _run = run;

	// Construct from an arbitrary OC algorithm. Mirrors upstream's
	// `OverclockingLogic logic = (p, v) -> ...` lambda construction -
	// used by the EBF recipe modifier to bind a per-recipe
	// HeatingCoilOC closure (capturing recipeTemp + machineTemp).
	public static OverclockingLogic Create(Func<OCParams, long, OCResult> run) =>
		new(run);

	public OCResult RunOverclockingLogic(OCParams ocParams, long maxVoltage) => _run(ocParams, maxVoltage);

	// Create a standard OverclockingLogic using either standardOC or
	// subTickParallelOC.
	public static OverclockingLogic Create(double durationFactor, double voltageFactor, bool subtick)
	{
		if (subtick)
			return new OverclockingLogic((p, maxV) => SubTickParallelOC(p, maxV, durationFactor, voltageFactor));
		return new OverclockingLogic((p, maxV) => StandardOC(p, maxV, durationFactor, voltageFactor));
	}

	public static readonly OverclockingLogic PERFECT_OVERCLOCK =
		Create(PERFECT_DURATION_FACTOR, STD_VOLTAGE_FACTOR, false);
	public static readonly OverclockingLogic NON_PERFECT_OVERCLOCK =
		Create(STD_DURATION_FACTOR, STD_VOLTAGE_FACTOR, false);

	public static readonly OverclockingLogic PERFECT_OVERCLOCK_SUBTICK =
		Create(PERFECT_DURATION_FACTOR, STD_VOLTAGE_FACTOR, true);
	public static readonly OverclockingLogic NON_PERFECT_OVERCLOCK_SUBTICK =
		Create(STD_DURATION_FACTOR, STD_VOLTAGE_FACTOR, true);

	// Determines overclocking parameters, runs the overclock, and returns a
	// ModifierFunction. shouldParallel defaults true (upstream's 3-arg
	// getModifier overload).
	public ModifierFunction GetModifier(IRecipeLogicMachine machine, GTRecipe recipe, long maxVoltage,
	                                    bool shouldParallel = true)
	{
		long EUt = RecipeHelper.GetRealEUt(recipe).GetTotalEU();
		if (EUt == 0) return ModifierFunction.IDENTITY;

		int recipeTier = VoltageTiers.TierByVoltage(EUt);
		int maximumTier = VoltageTiers.OcTierByVoltage(maxVoltage);
		int OCs = maximumTier - recipeTier;
		if (recipeTier == (int)VoltageTier.ULV) OCs--;
		if (OCs == 0) return ModifierFunction.IDENTITY;

		int maxParallels;
		if (!shouldParallel || this == PERFECT_OVERCLOCK || this == NON_PERFECT_OVERCLOCK)
		{
			// don't parallel
			maxParallels = 1;
		}
		else
		{
			// lg = floor(log_4(duration)), how many OCs it takes to get
			// duration < 4 with perfect duration factor.
			int lg = Log2Floor(recipe.Duration) / 2;
			if (lg > OCs)
			{
				maxParallels = 16;
			}
			else
			{
				int p = SaturatedCast((1L << (2 * (OCs - lg))) + 1);
				maxParallels = ParallelLogic.GetParallelAmount(machine, recipe, p);
			}
		}

		var ocParams = new OCParams(EUt, recipe.Duration, OCs, maxParallels);
		var result = RunOverclockingLogic(ocParams, maxVoltage);
		return result.ToModifier();
	}

	// Port of OverclockingLogic.OCResult.toModifier (the record body is in the
	// Algorithms partial; toModifier is here because it needs ModifierFunction).
	public sealed partial record OCResult
	{
		public ModifierFunction ToModifier() =>
			ModifierFunction.Builder()
				.ModifyAllContents(ContentModifier.Multiplier_(Parallels))
				.EutMultiplier(EutMultiplier)
				.DurationMultiplier(DurationMultiplier)
				.AddOCs(OcLevel)
				.SubtickParallels(Parallels)
				.Build();
	}
}
