using Xunit;
using OL = GregTechCEuTerraria.Api.Recipe.OverclockingLogic;

namespace GregTechCEuTerraria.Tests;

// Port of the testable intent of upstream's
// com.gregtechceu.gtceu.api.recipe.OverclockLogicTest.
//
// Upstream's test is a Minecraft @GameTest (live server + placed multiblocks).
// Its machine-tick / power-consumption cases can't be unit-tested off-game,
// but its overclock-math cases (apply{Perfect,NonPerfect,...}OverclockTest)
// reduce to the pure OverclockingLogic algorithms - exercised directly here on
// OCParams / OCResult. The OC tier-gate (EV-recipe-on-HV-machine cancels) is
// covered as a VoltageTiers tier comparison.
public class OverclockingLogicTests
{
	// Realistic GT voltages (GTValues.V[...]).
	private const long V_MV = 128;
	private const long V_HV = 512;

	// Upstream overclockLogicApplyPerfectOverclockTest - an HV machine
	// overclocks an MV recipe once: duration /4, EU x4.
	[Fact]
	public void PerfectOverclock_QuartersDuration_QuadruplesEu()
	{
		var p = new OL.OCParams(V_MV, Duration: 100, OcAmount: 1, MaxParallels: 1);
		var r = OL.StandardOC(p, maxVoltage: V_HV, OL.PERFECT_DURATION_FACTOR, OL.STD_VOLTAGE_FACTOR);

		Assert.Equal(1, r.OcLevel);
		Assert.Equal(OL.STD_VOLTAGE_FACTOR, r.EutMultiplier, 9);
		Assert.Equal(1.0 / OL.PERFECT_DURATION_FACTOR_INV, r.DurationMultiplier, 9);
		Assert.Equal(1, r.Parallels);
	}

	// Upstream overclockLogicApplyNonPerfectOverclockTest - duration /2, EU x4.
	[Fact]
	public void NonPerfectOverclock_HalvesDuration_QuadruplesEu()
	{
		var p = new OL.OCParams(V_MV, Duration: 100, OcAmount: 1, MaxParallels: 1);
		var r = OL.StandardOC(p, maxVoltage: V_HV, OL.STD_DURATION_FACTOR, OL.STD_VOLTAGE_FACTOR);

		Assert.Equal(1, r.OcLevel);
		Assert.Equal(OL.STD_VOLTAGE_FACTOR, r.EutMultiplier, 9);
		Assert.Equal(1.0 / OL.STD_DURATION_FACTOR_INV, r.DurationMultiplier, 9);
	}

	// Upstream overclockLogicApplyNonPerfectNonParallel1tOverclockTest - a 1-tick
	// recipe can't be sped up by a non-subtick OC, so nothing changes.
	[Fact]
	public void NonPerfectOverclock_OneTickRecipe_DoesNotOverclock()
	{
		var p = new OL.OCParams(V_MV, Duration: 1, OcAmount: 1, MaxParallels: 1);
		var r = OL.StandardOC(p, maxVoltage: V_HV, OL.STD_DURATION_FACTOR, OL.STD_VOLTAGE_FACTOR);

		Assert.Equal(0, r.OcLevel);
		Assert.Equal(1.0, r.EutMultiplier, 9);
		Assert.Equal(1.0, r.DurationMultiplier, 9);
	}

	// Upstream overclockLogicApplyPerfectParallelOverclockTest - a 1-tick recipe
	// can't drop below 1 tick, so a subtick OC parallelizes instead: x4.
	[Fact]
	public void PerfectSubtickOverclock_QuadruplesParallels()
	{
		var p = new OL.OCParams(V_MV, Duration: 1, OcAmount: 1, MaxParallels: 16);
		var r = OL.SubTickParallelOC(p, maxVoltage: V_HV, OL.PERFECT_DURATION_FACTOR, OL.STD_VOLTAGE_FACTOR);

		Assert.Equal(1, r.OcLevel);
		Assert.Equal((int)OL.PERFECT_DURATION_FACTOR_INV, r.Parallels);
		Assert.Equal(OL.STD_VOLTAGE_FACTOR, r.EutMultiplier, 9);
	}

	// Upstream overclockLogicApplyNonPerfectParallelOverclockTest - subtick OC
	// on a 1-tick recipe parallelizes x2.
	[Fact]
	public void NonPerfectSubtickOverclock_DoublesParallels()
	{
		var p = new OL.OCParams(V_MV, Duration: 1, OcAmount: 1, MaxParallels: 16);
		var r = OL.SubTickParallelOC(p, maxVoltage: V_HV, OL.STD_DURATION_FACTOR, OL.STD_VOLTAGE_FACTOR);

		Assert.Equal(1, r.OcLevel);
		Assert.Equal((int)OL.STD_DURATION_FACTOR_INV, r.Parallels);
		Assert.Equal(OL.STD_VOLTAGE_FACTOR, r.EutMultiplier, 9);
	}

	// Two overclocks compound: EU x4^2, duration x0.5^2.
	[Fact]
	public void StandardOc_TwoOverclocks_CompoundMultipliers()
	{
		var p = new OL.OCParams(V_MV, Duration: 100, OcAmount: 2, MaxParallels: 1);
		// maxVoltage high enough for 2 OCs (128 -> 512 -> 2048).
		var r = OL.StandardOC(p, maxVoltage: 8192, OL.STD_DURATION_FACTOR, OL.STD_VOLTAGE_FACTOR);

		Assert.Equal(2, r.OcLevel);
		Assert.Equal(16.0, r.EutMultiplier, 9);
		Assert.Equal(0.25, r.DurationMultiplier, 9);
	}

	// The OC loop stops once another EU multiply would exceed maxVoltage, even
	// with overclocks still budgeted.
	[Fact]
	public void StandardOc_StopsAtVoltageCap()
	{
		// 128 -> 512 -> 2048; cap at 512 allows exactly one OC.
		var p = new OL.OCParams(V_MV, Duration: 1000, OcAmount: 5, MaxParallels: 1);
		var r = OL.StandardOC(p, maxVoltage: V_HV, OL.STD_DURATION_FACTOR, OL.STD_VOLTAGE_FACTOR);

		Assert.Equal(1, r.OcLevel);
	}

	// The non-subtick loop stops once duration would drop below 1 tick.
	[Fact]
	public void StandardOc_StopsWhenDurationWouldDropBelowOne()
	{
		// duration 4 -> 2 -> 1; a third halving would be 0.5, so OC stops at 2.
		var p = new OL.OCParams(V_MV, Duration: 4, OcAmount: 5, MaxParallels: 1);
		var r = OL.StandardOC(p, maxVoltage: long.MaxValue, OL.STD_DURATION_FACTOR, OL.STD_VOLTAGE_FACTOR);

		Assert.Equal(2, r.OcLevel);
		Assert.Equal(0.25, r.DurationMultiplier, 9);
	}

	// Subtick non-parallel OC keeps duration at the 1-tick floor by spending
	// the overclock on extra EU efficiency instead.
	[Fact]
	public void SubTickNonParallelOc_OneTickRecipe_StillOverclocks()
	{
		var p = new OL.OCParams(V_MV, Duration: 1, OcAmount: 1, MaxParallels: 1);
		var r = OL.SubTickNonParallelOC(p, maxVoltage: V_HV, OL.STD_DURATION_FACTOR, OL.STD_VOLTAGE_FACTOR);

		Assert.Equal(1, r.OcLevel);
		// EUt xvoltageFactor then xdurationFactor for the sub-1-tick step.
		Assert.Equal(OL.STD_VOLTAGE_FACTOR * OL.STD_DURATION_FACTOR, r.EutMultiplier, 9);
		Assert.Equal(1.0, r.DurationMultiplier, 9);
	}

	// HeatingCoilOC does perfect OCs first (one per 2 coil-discount steps),
	// then standard OCs.
	[Fact]
	public void HeatingCoilOc_AppliesPerfectThenStandard()
	{
		// (2700-900)/900 = 2 discount steps -> 1 perfect OC available.
		var p = new OL.OCParams(V_MV, Duration: 1000, OcAmount: 2, MaxParallels: 1);
		var r = OL.HeatingCoilOC(p, maxVoltage: 8192, recipeTemp: 900, machineTemp: 2700);

		Assert.Equal(2, r.OcLevel);
		Assert.Equal(16.0, r.EutMultiplier, 9);
		// First OC perfect (x0.25 duration), second standard (x0.5).
		Assert.Equal(OL.PERFECT_DURATION_FACTOR * OL.STD_DURATION_FACTOR, r.DurationMultiplier, 9);
	}

	[Fact]
	public void GetCoilEUtDiscount_BelowThreshold_NoDiscount()
	{
		Assert.Equal(1.0, OL.GetCoilEUtDiscount(recipeTemp: 500, machineTemp: 5000), 9);
		// At/above threshold but machine no hotter than recipe -> still no discount.
		Assert.Equal(1.0, OL.GetCoilEUtDiscount(recipeTemp: 900, machineTemp: 900), 9);
	}

	[Fact]
	public void GetCoilEUtDiscount_AppliesPoint95PerStep()
	{
		// (2800-1000)/900 = 2 steps -> 0.95^2.
		Assert.Equal(0.9025, OL.GetCoilEUtDiscount(recipeTemp: 1000, machineTemp: 2800), 6);
	}
}
