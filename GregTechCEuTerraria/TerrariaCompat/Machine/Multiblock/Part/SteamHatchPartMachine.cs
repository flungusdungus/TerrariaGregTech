#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine.Trait;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

// Port of SteamHatchPartMachine. Tier-0 (ULV) IN fluid hatch, steam-filtered,
// 64-bucket / 1 slot. Used by steam_oven etc.
public class SteamHatchPartMachine : FluidHatchPartMachine
{
	public const int INITIAL_TANK_CAPACITY = 64 * BUCKET_VOLUME;

	protected override string Label => "Steam Hatch";

	public SteamHatchPartMachine() : base() { }

	// Pin upstream's hard-coded (tier 0, IN, 64 buckets, 1 slot) regardless of
	// the def's PartFluidSlots - skip base which would use the 1x capacity table.
	protected override void OnDefinitionBound()
	{
		Configure(
			tier: 0,
			io: IO.IN,
			initialCapacity: INITIAL_TANK_CAPACITY,
			slots: 1);
	}

	protected override NotifiableFluidTank CreateTank(int initialCapacity, int slots)
	{
		var tank = base.CreateTank(initialCapacity, slots);
		tank.SetFilter(stack => !stack.IsEmpty && stack.Type!.Id == FluidRegistry.Steam.Id);
		return tank;
	}
}
