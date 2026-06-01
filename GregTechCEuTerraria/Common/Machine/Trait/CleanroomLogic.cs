#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Machine.Feature;
using GregTechCEuTerraria.Api.Machine.Feature.Multiblock;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Electric;
using Terraria;
using Status = GregTechCEuTerraria.Api.Machine.Feature.RecipeLogicStatus;

namespace GregTechCEuTerraria.Common.Machine.Trait;

// Port of com.gregtechceu.gtceu.common.machine.trait.CleanroomLogic.
//
// Subclass of RecipeLogic that runs the Cleanroom multi's custom cleanliness
// cycle (no actual recipes): drains EU from the bound energy container, and
// every `duration` ticks calls into the controller's `AdjustCleanAmount` to
// ratchet cleanliness up (when running) or down (on power-fail / all
// problems). When cleanliness >= 95 -> `CleanroomProviderTrait.IsActive = true`.
//
// Adaptations: the EnvironmentalHazardSavedData zone check is DROPPED (hazard
// subsystem deferred - the cleanroom just doesn't react to pollution zones, of
// which there are none today); setWaiting(Component) -> SetWaiting(string);
// typed Machine accessor + ValidMachineClasses constrain to CleanroomMachine.
// BASE_CLEAN_AMOUNT, the adjustCleanAmount / consumeEnergy formulas, the
// >=6-problems stop, and progress regression are verbatim (see methods below).
public sealed class CleanroomLogic : RecipeLogic
{
	public const int BASE_CLEAN_AMOUNT = 2;

	public IMaintenanceMachine? MaintenanceMachine { get; set; }
	public IEnergyContainer?    EnergyContainer    { get; set; }
	public bool                 IsActiveAndNeedsUpdate { get; set; }

	public CleanroomLogic() : base() { }

	public new CleanroomMachine Machine => (CleanroomMachine)base.Machine;

	protected override IReadOnlyList<Type> ValidMachineClasses() =>
		new[] { typeof(CleanroomMachine) };

	// Override RecipeLogic.ServerTick with the cleanroom's custom cleanliness
	// cycle. Verbatim port of upstream's serverTick.
	public override void ServerTick()
	{
		if (_duration <= 0) return;

		// Gate to upstream's 20 Hz MC cadence (mirrors RecipeLogic.ServerTick:199).
		// CleanroomLogic OVERRIDES the base without calling it, so the base gate
		// doesn't apply - the override needs its own. Without this, `_progress++`
		// + `AdjustCleanAmount` + EU drain all run 3x too fast at SimSpeed=1.0.
		if (Main.GameUpdateCount % (uint)global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(1) != 0) return;

		// Upstream consults EnvironmentalHazardSavedData here for nearby
		// pollution zones; not ported (hazard subsystem deferred).

		// "machine does not run" branch: any maintenance hatch reports <6
		// problems-fixed (i.e. has at least one open problem).
		bool maintenanceOk = MaintenanceMachine == null ||
		                     MaintenanceMachine.GetNumMaintenanceProblems() < 6;

		if (maintenanceOk)
		{
			// Drain the energy
			if (!ConsumeEnergy())
			{
				if (_progress > 0 && ((IRecipeLogicMachine)Machine).RegressWhenWaiting())
					_progress = 1;
				if (Machine.GetMcOffsetTimer() % _duration == 0)
					AdjustCleanAmount(declined: true);
				SetWaiting("Insufficient energy");
				return;
			}
			SetStatus(Status.WORKING);
			if (_progress++ < _duration)
			{
				if (!Machine.OnWorking()) InterruptRecipe();
				return;
			}
			_progress = 0;
			if (!Machine.BeforeWorking(null!)) return;
			AdjustCleanAmount(declined: false);
		}
		else
		{
			// All maintenance problems - cleanroom regresses + idles.
			if (_progress > 0) _progress--;
			if (Machine.GetOffsetTimer() % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(_duration) == 0)
				AdjustCleanAmount(declined: true);
			SetStatus(Status.IDLE);
			Machine.AfterWorking();
		}
	}

	private void AdjustCleanAmount(bool declined)
	{
		// 5..~44% per cycle when ascending.
		int amount = BASE_CLEAN_AMOUNT + 3 * (GetTierDifference() + 1);
		if (declined) amount = -amount;
		if (MaintenanceMachine != null) amount -= MaintenanceMachine.GetNumMaintenanceProblems();
		Machine.AdjustCleanAmount(amount);
	}

	private bool ConsumeEnergy()
	{
		var providerTrait = Machine.Traits.GetTrait(CleanroomProviderTrait.TYPE);
		if (providerTrait == null) return false;
		int tier = System.Math.Clamp(Machine.MultiTier, (int)VoltageTier.ULV, (int)VoltageTier.MAX);
		// Once we're clean, only sip 3/16 amp; while cleaning, full amperage.
		long energyToDrain = providerTrait.IsActive
			? System.Math.Max(8, 3 * VoltageTiers.V(tier) / 16)
			: VoltageTiers.VA(tier);
		if (EnergyContainer == null) return false;
		long result = EnergyContainer.EnergyStored - energyToDrain;
		if (result >= 0L && result <= EnergyContainer.EnergyCapacity)
		{
			EnergyContainer.RemoveEnergy(energyToDrain);
			return true;
		}
		return false;
	}

	private int GetTierDifference()
	{
		const int minEnergyTier = (int)VoltageTier.LV;
		return System.Math.Max(0, Machine.MultiTier - minEnergyTier);
	}

	// Sync the real cycling `_progress` to clients (mirrors upstream's
	// @DescSynced progress). RecipeLogic.SaveForSync OMITS _progress as a
	// bandwidth optimization, relying on the client to interpolate it via
	// OnClientTick and reset it on the next WORKING->IDLE (or _consecutiveRecipes)
	// transition. The cleanroom cycle NEVER triggers that reset: it loops
	// `_progress 0->_duration->0` INLINE (ServerTick:99) while status stays WORKING
	// and _consecutiveRecipes never changes (there is no recipe). So a client
	// would ramp _progress to _duration and stick at 100% forever - the
	// "Progress 6.50s / 6.50s (100%)" visual freeze. Sending the full Save blob
	// (which includes _progress) lets the broadcast carry the cycling value;
	// OnClientTick still smooths between the ~6-tick broadcasts and each
	// broadcast corrects it, including the per-cycle reset back to ~0.
	public override void SaveForSync(Terraria.ModLoader.IO.TagCompound tag) => Save(tag);

	// Public so the controller's `OnStructureFormed` can set the dynamic
	// duration computed from room area.
	public void SetDuration(int max) { _duration = max; }
	public int  GetDuration() => _duration;
}
