#nullable enable
using System;
using GregTechCEuTerraria.Common.Energy;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

// Verbatim port of TieredWorkableElectricMultiblockMachine.
// WorkableElectricMultiblockMachine + fixed per-instance tier cap. Used when
// the controller block carries a tier (fusion_reactor / fluid_drilling_rig /
// large_miner LV/MV/HV variants). Clamps GetMaxVoltage so an MV controller
// can't be tricked into IV recipes via EV hatches.
public abstract class TieredWorkableElectricMultiblockMachine : WorkableElectricMultiblockMachine
{
	public int HardwareTier { get; private set; }

	// Player-selectable overclock cap (currently == HardwareTier; SetOverclockTier
	// rejects out-of-range).
	public int CurrentOverclockTier { get; protected set; }

	protected TieredWorkableElectricMultiblockMachine() : base() { }

	public void BindHardwareTier(int tier)
	{
		HardwareTier = tier;
		CurrentOverclockTier = tier;
	}

	public new int MinOverclockTier => 0;
	public new int MaxOverclockTier => HardwareTier;

	public new void SetOverclockTier(int tier)
	{
		if (!IsServer) return;
		if (tier < MinOverclockTier || tier > MaxOverclockTier) return;
		CurrentOverclockTier = tier;
		Recipe.MarkLastRecipeDirty();
	}

	public new long OverclockVoltage =>
		Math.Min(VoltageTiers.Voltage((VoltageTier)CurrentOverclockTier), base.OverclockVoltage);

	public new int GetTier() => Math.Min(HardwareTier, base.GetTier());

	public override long GetMaxVoltage() =>
		Math.Min(VoltageTiers.Voltage((VoltageTier)HardwareTier), base.GetMaxVoltage());
}
