#nullable enable
using System;
using GregTechCEuTerraria.Api.Recipe;

namespace GregTechCEuTerraria.Api.Machine.Feature.Multiblock;

// Port of com.gregtechceu.gtceu.api.machine.feature.multiblock.
// IMaintenanceMachine.
//
// Contract for a multiblock part that owns the maintenance state - the
// 6-bit problems byte (wrench/screwdriver/soft-mallet/hard-hammer/wire-
// cutter/crowbar), the duct-taped flag, and the "time active" counter that
// drives random problem injection. Used by the EBF / Distillation Tower /
// Vacuum Freezer / etc. controllers to gate recipes and apply a duration
// penalty when problems are present.
//
// Adaptations: MAINTENANCE_TAPED_PROPERTY (Forge BlockState) dropped - IsTaped
// kept as plain state; Component -> string + fancy-tooltip hooks -> IFancyTooltip;
// ConfigHolder.enableMaintenance / maintenanceCheckRate -> MaintenanceConfig
// (compile-time); GTValues.RNG -> per-interface Rng. The 6-bit layout, 1-in-6000
// RNG trigger, popcount math, and OnWorking/ModifyRecipe veto are all verbatim.
public interface IMaintenanceMachine : IMultiPart
{
	const byte ALL_PROBLEMS = 0;
	const byte NO_PROBLEMS  = 0b111111;

	private static readonly Random Rng = new();

	// Maintenance-hatch flavour: true = the "Full Auto" variant (no problems
	// ever, no tape required). Used by `CalculateMaintenance` to short-circuit
	// the problem-injection RNG.
	bool IsFullAuto();

	// Duct-taped flag. Once taped, every problem is fixed in one go (see
	// `MaintenanceHatchPartMachine.update`); upstream reads this for the
	// IS_TAPED BlockState render variant.
	bool IsTaped();
	void SetTaped(bool isTaped);

	// Initial value `MaintenanceProblems` takes when this hatch is first
	// placed. Default hatches return `ALL_PROBLEMS` (every problem set);
	// auto-hatches return `NO_PROBLEMS`.
	byte StartProblems();

	// Bitfield - bit `n` set means problem `n` is FIXED. So `0b111111` = no
	// problems; `0` = all six problems present. See upstream comment for the
	// per-bit tool mapping.
	byte GetMaintenanceProblems();
	void SetMaintenanceProblems(byte problems);

	// Tick counter - how long this hatch's controller has been running.
	// Increments via `CalculateMaintenance`; resets to 0 when the check
	// fires (every `CheckRate` ticks).
	int GetTimeActive();
	void SetTimeActive(int time);

	// Recipe-duration multiplier - the Configurable Maintenance Hatch lets
	// the player trade duration for time-multiplier (more problems faster).
	// Default 1.0 = no change.
	float GetDurationMultiplier() => 1f;

	// Higher duration multiplier -> lower time multiplier -> faster problem
	// injection. Default 1.0; the Configurable hatch overrides.
	float GetTimeMultiplier() => 1f;

	// A maintenance hatch belongs to exactly one controller.
	new bool CanShared() => false;

	// Tick-driven problem injection. Adds `duration` ticks to TimeActive;
	// when the accumulator exceeds `CheckRate / timeMultiplier`, rolls
	// 1-in-6000 and on hit applies one random problem + clears the taped
	// flag. No-op when the subsystem is config-disabled or the hatch is
	// full-auto.
	void CalculateMaintenance(IMaintenanceMachine maintenanceMachine, int duration)
	{
		if (!MaintenanceConfig.Enabled || maintenanceMachine.IsFullAuto())
			return;

		SetTimeActive(GetTimeActive() + duration);
		float rate = MaintenanceConfig.CheckRate / maintenanceMachine.GetTimeMultiplier();
		if (GetTimeActive() >= rate)
		{
			SetTimeActive(0);
			if (Rng.Next(6000) == 0)
			{
				CauseRandomMaintenanceProblems();
				maintenanceMachine.SetTaped(false);
			}
		}
	}

	void CalculateMaintenance(IMaintenanceMachine maintenanceMachine) =>
		CalculateMaintenance(maintenanceMachine, 1);

	int GetNumMaintenanceProblems() =>
		MaintenanceConfig.Enabled ? 6 - PopCount(GetMaintenanceProblems()) : 0;

	bool HasMaintenanceProblems() =>
		MaintenanceConfig.Enabled && GetMaintenanceProblems() < 63;

	// Mark problem `index` (0..5) as fixed - sets bit `index` in the
	// problems byte.
	void SetMaintenanceFixed(int index) =>
		SetMaintenanceProblems((byte)(GetMaintenanceProblems() | (byte)(1 << index)));

	// Clear one random problem's "fixed" bit - i.e. inject a new problem at
	// a random tool index.
	void CauseRandomMaintenanceProblems() =>
		SetMaintenanceProblems((byte)(GetMaintenanceProblems() & (byte)~(1 << Rng.Next(6))));

	// === IMultiPart lifecycle overrides =====================================

	// Called once per controller tick. Drives the problem-injection RNG and
	// - if problems are present - marks the last recipe dirty so the
	// controller recomputes (and stalls until fixed).
	new bool OnWorking(IWorkableMultiController controller)
	{
		CalculateMaintenance(this);
		if (HasMaintenanceProblems())
			controller.GetRecipeLogic().MarkLastRecipeDirty();
		return true;
	}

	// Recipe veto + duration scaling. Returns null (= "veto this recipe")
	// while any problem is present; otherwise applies the duration
	// multiplier when non-1.
	new GTRecipe? ModifyRecipe(GTRecipe recipe)
	{
		if (MaintenanceConfig.Enabled)
		{
			if (HasMaintenanceProblems())
				return null;
			var durationMultiplier = GetDurationMultiplier();
			if (durationMultiplier != 1f)
			{
				recipe = recipe.Copy();
				recipe.Duration = (int)(recipe.Duration * durationMultiplier);
			}
		}
		return recipe;
	}

	// Inline popcount - System.Numerics.BitOperations is .NET-only; this
	// keeps the interface dep-free.
	private static int PopCount(byte b)
	{
		int count = 0;
		while (b != 0) { count++; b &= (byte)(b - 1); }
		return count;
	}
}

// Compile-time gate mirroring upstream's ConfigHolder.enableMaintenance /
// maintenanceCheckRate (field-backed once a runtime-config layer lands).
// Enabled = false for now: the whole problem-injection + recipe-veto loop is
// dormant (all the hatch/cover/fix code stays in place) - one-line flip to
// re-enable once a maintenance-hatch UI lands.
public static class MaintenanceConfig
{
	public static readonly bool Enabled = false;

	// Ticks between problem-injection rolls. Upstream default 100 (5 seconds
	// at 20 tps); the divisor is the hatch's `GetTimeMultiplier`.
	public const int CheckRate = 100;
}
