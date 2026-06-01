#nullable enable
using System;
using GregTechCEuTerraria.Api.Machine.Feature;

namespace GregTechCEuTerraria.Api.Recipe.Modifier;

// Verbatim port of com.gregtechceu.gtceu.api.recipe.modifier.Efficiency
// Modifier. Scales recipe duration based on consecutive-run count.
//
// Multiplies duration by `baseMultiplier x efficiency^runs`, clamped above
// by `hardCap`. `heuristic = 300 x efficiency^2` is the run-count threshold
// past which the formula is short-circuited to `hardCap` (avoids deep
// `Math.Pow` calls once the cap is mathematically guaranteed).
//
// Status: ported for parity completeness, but UNUSED by any concrete machine
// in upstream - no GTRecipeModifiers entry or MachineBuilder call attaches
// it. Available for future content / datapacks.
//
// Documented adaptation:
//   - Guava `Preconditions.checkArgument` -> ArgumentOutOfRangeException
//     (closest C# equivalent; same fail-fast semantics).
public class EfficiencyModifier : RecipeModifier
{
	private readonly double _baseMultiplier;
	private readonly double _efficiency;
	private readonly double _hardCap;
	private readonly double _heuristic;

	private EfficiencyModifier(double baseMultiplier, double efficiency, double hardCap) : base()
	{
		if (baseMultiplier <= 0)
			throw new ArgumentOutOfRangeException(nameof(baseMultiplier),
				$"Base multiplier must be > 0: {baseMultiplier}");
		if (efficiency <= 0)
			throw new ArgumentOutOfRangeException(nameof(efficiency),
				$"Efficiency must be > 0: {efficiency}");
		if (hardCap < 0)
			throw new ArgumentOutOfRangeException(nameof(hardCap),
				$"Hard cap must be >= 0: {hardCap}");
		_baseMultiplier = baseMultiplier;
		_efficiency     = efficiency;
		_hardCap        = hardCap;
		_heuristic      = 300 * efficiency * efficiency;
	}

	public static EfficiencyModifier Of(double baseMultiplier, double efficiency, double hardCap) =>
		new(baseMultiplier, efficiency, hardCap);

	public static EfficiencyModifier Of(double baseMultiplier, double efficiency) =>
		Of(baseMultiplier, efficiency, 0.5);

	public static EfficiencyModifier Of(double efficiency) =>
		Of(2, efficiency, 0.5);

	// Efficiency recipe modifier:
	//   duration *= max(hardCap, baseMultiplier x efficiency^runs)
	public override ModifierFunction GetModifier(IRecipeLogicMachine machine, GTRecipe recipe)
	{
		// Upstream uses `instanceof IRecipeLogicMachine` - our delegate is already
		// typed IRecipeLogicMachine, so the type check is a no-op. Kept the
		// duration <= 1 short-circuit verbatim.
		if (recipe.Duration <= 1) return ModifierFunction.IDENTITY;
		int runs = machine.GetRecipeLogic().GetConsecutiveRecipes();
		double mult;
		// Heuristic to avoid deep Math.Pow once the cap is mathematically
		// guaranteed - "if you need more than this to get to the cap, seek help".
		if (runs > _heuristic) mult = _hardCap;
		else mult = Math.Max(_hardCap, _baseMultiplier * Math.Pow(_efficiency, runs));
		return ModifierFunction.Builder()
			.DurationMultiplier(mult)
			.Build();
	}
}
