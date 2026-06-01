#nullable enable
using System;
using System.Numerics;

namespace GregTechCEuTerraria.Api.Recipe;

// Pure-math half of OverclockingLogic - see OverclockingLogic.cs for the
// instance / GetModifier / RecipeModifier-facing half.
//
// Split-file adaptation: upstream OverclockingLogic.java is a single file. The
// standalone overclock algorithms below depend on nothing but the OCParams /
// OCResult number tuples, so they live in this partial - that lets the
// pure-logic test project (no tML / XNA) compile and exercise them without
// dragging in the recipe / machine / capability graph. Behaviour is identical
// to a single file; the split is purely physical (and greppable 1:1 against
// the upstream method bodies).
public sealed partial class OverclockingLogic
{
	public const double STD_VOLTAGE_FACTOR = 4.0;
	public const double PERFECT_HALF_VOLTAGE_FACTOR = 2.0;

	public const double STD_DURATION_FACTOR = 0.5;
	public const double STD_DURATION_FACTOR_INV = 2.0;

	public const double PERFECT_DURATION_FACTOR = 0.25;
	public const double PERFECT_DURATION_FACTOR_INV = 4.0;

	public const double PERFECT_HALF_DURATION_FACTOR = 0.5;
	public const double PERFECT_HALF_DURATION_FACTOR_INV = 2.0;

	public const int COIL_EUT_DISCOUNT_TEMPERATURE = 900;

	// Standard overclocking algorithm with no sub-tick behavior. While there
	// are overclocks remaining: multiply EUt by voltageFactor, multiply
	// duration by durationFactor, stop early if duration would drop below 1.
	public static OCResult StandardOC(OCParams ocParams, long maxVoltage, double durationFactor, double voltageFactor)
	{
		double duration = ocParams.Duration;
		double eut = ocParams.Eut;
		int ocAmount = ocParams.OcAmount;
		int ocLevel = 0;

		while (ocAmount-- > 0)
		{
			// Check if EUt can be multiplied without going over the max.
			double potentialEUt = eut * voltageFactor;
			if (potentialEUt > maxVoltage) break;

			// Check if duration can be multiplied without going below 1.
			double potentialDuration = duration * durationFactor;
			if (potentialDuration < 1) break;
			duration = potentialDuration;

			// Only set EUt after checking duration.
			eut = potentialEUt;
			ocLevel++;
		}
		return new OCResult(Math.Pow(voltageFactor, ocLevel), Math.Pow(durationFactor, ocLevel), ocLevel, 1);
	}

	// Overclocking algorithm with sub-tick logic - improves energy efficiency
	// without parallelization. For overclocks that would drop duration below 1,
	// multiply EUt by durationFactor and hold duration at 1.
	public static OCResult SubTickNonParallelOC(OCParams ocParams, long maxVoltage, double durationFactor,
	                                            double voltageFactor)
	{
		double duration = ocParams.Duration;
		double eut = ocParams.Eut;
		int ocAmount = ocParams.OcAmount;

		int ocLevel = 0;
		double eutMultiplier = 1;
		double durationMultiplier = 1;

		while (ocAmount-- > 0)
		{
			double potentialEUt = eut * voltageFactor;
			if (potentialEUt > maxVoltage || potentialEUt < 1) break;
			eutMultiplier *= voltageFactor;

			double potentialDuration = duration * durationFactor;
			if (potentialDuration < 1)
			{
				potentialEUt = eut * durationFactor;
				if (potentialEUt > maxVoltage || potentialEUt < 1) break;
				eutMultiplier *= durationFactor;
			}
			else
			{
				duration = potentialDuration;
				durationMultiplier *= durationFactor;
			}

			eut = potentialEUt;
			ocLevel++;
		}

		return new OCResult(eutMultiplier, durationMultiplier, ocLevel, 1);
	}

	// Overclocking algorithm with sub-tick parallelization. For overclocks that
	// would drop duration below 1, parallelize instead (1 / durationFactor per
	// overclock).
	public static OCResult SubTickParallelOC(OCParams ocParams, long maxVoltage, double durationFactor,
	                                         double voltageFactor)
	{
		double duration = ocParams.Duration;
		double eut = ocParams.Eut;
		int ocAmount = ocParams.OcAmount;
		int maxParallels = ocParams.MaxParallels;

		double parallel = 1;
		bool shouldParallel = false;
		int ocLevel = 0;
		double durationMultiplier = 1;

		while (ocAmount-- > 0)
		{
			// Check if EUt can be multiplied again without going over the max.
			double potentialEUt = eut * voltageFactor;
			if (potentialEUt > maxVoltage) break;

			// If already doing parallels or duration would go below 1, parallel.
			if (shouldParallel || duration * durationFactor < 1)
			{
				double potentialParallel = parallel / durationFactor;
				if (potentialParallel > maxParallels) break;
				parallel = potentialParallel;
				shouldParallel = true;
			}
			else
			{
				duration *= durationFactor;
				durationMultiplier *= durationFactor;
			}

			// Only set EUt after checking parallels.
			eut = potentialEUt;
			ocLevel++;
		}

		return new OCResult(Math.Pow(voltageFactor, ocLevel), durationMultiplier, ocLevel, (int)parallel);
	}

	// Heating Coil overclocking algorithm with sub-tick parallelization. Does
	// perfect OCs first (PERFECT_DURATION_FACTOR), then standard. The number of
	// perfect OCs is getCoilDiscountAmount / 2.
	public static OCResult HeatingCoilOC(OCParams ocParams, long maxVoltage, int recipeTemp, int machineTemp)
	{
		int perfectOCAmount = GetCoilDiscountAmount(recipeTemp, machineTemp) / 2;
		double duration = ocParams.Duration;
		double eut = ocParams.Eut;
		int ocAmount = ocParams.OcAmount;
		int maxParallels = ocParams.MaxParallels;

		double parallel = 1;
		bool shouldParallel = false;
		int ocLevel = 0;
		double durationMultiplier = 1;

		while (ocAmount-- > 0)
		{
			// Do perfects first if possible.
			bool perfect = perfectOCAmount-- > 0;

			double potentialEUt = eut * STD_VOLTAGE_FACTOR;
			if (potentialEUt > maxVoltage) break;

			double dFactor = perfect ? PERFECT_DURATION_FACTOR : STD_DURATION_FACTOR;
			if (shouldParallel || duration * dFactor < 1)
			{
				double pFactor = perfect ? PERFECT_DURATION_FACTOR_INV : STD_DURATION_FACTOR_INV;
				double potentialParallel = parallel * pFactor;
				if (potentialParallel > maxParallels) break;
				parallel = potentialParallel;
				shouldParallel = true;
			}
			else
			{
				duration *= dFactor;
				durationMultiplier *= dFactor;
			}

			eut = potentialEUt;
			ocLevel++;
		}

		return new OCResult(Math.Pow(STD_VOLTAGE_FACTOR, ocLevel), durationMultiplier, ocLevel, (int)parallel);
	}

	// Finds the coil discount amount based on recipe / machine temp.
	private static int GetCoilDiscountAmount(int recipeTemp, int machineTemp) =>
		Math.Max(0, (machineTemp - recipeTemp) / COIL_EUT_DISCOUNT_TEMPERATURE);

	// Calculates the heating coil EU/t discount multiplier.
	public static double GetCoilEUtDiscount(int recipeTemp, int machineTemp)
	{
		if (recipeTemp < COIL_EUT_DISCOUNT_TEMPERATURE) return 1;
		int amountEUtDiscount = GetCoilDiscountAmount(recipeTemp, machineTemp);
		if (amountEUtDiscount < 1) return 1;
		return Math.Min(1, Math.Pow(0.95, amountEUtDiscount));
	}

	// IntMath.log2(n, FLOOR) - position of the highest set bit. n >= 1.
	private static int Log2Floor(int n) => BitOperations.Log2((uint)Math.Max(1, n));

	// GTMath.saturatedCast - clamp a long to the int range.
	private static int SaturatedCast(long value) =>
		value > int.MaxValue ? int.MaxValue : value < int.MinValue ? int.MinValue : (int)value;

	// Port of OverclockingLogic.OCParams record.
	public sealed record OCParams(long Eut, int Duration, int OcAmount, int MaxParallels);

	// Port of OverclockingLogic.OCResult record. toModifier() lives in the
	// other partial (it needs ModifierFunction).
	public sealed partial record OCResult(double EutMultiplier, double DurationMultiplier, int OcLevel, int Parallels);
}
