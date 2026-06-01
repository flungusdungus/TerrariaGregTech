#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine.Trait;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

// Port of PumpHatchPartMachine. ULV OUT fluid hatch, water-filtered,
// 1-bucket / 1 slot. Surface fixture for the Pump multi.
public class PumpHatchPartMachine : FluidHatchPartMachine
{
	public const int INITIAL_TANK_CAPACITY = 1 * BUCKET_VOLUME;

	protected override string Label => "Pump Hatch";

	public PumpHatchPartMachine() : base() { }

	public void Configure() => Configure(
		tier: 0,
		io: IO.OUT,
		initialCapacity: INITIAL_TANK_CAPACITY,
		slots: 1);

	// Pin 1-bucket regardless of generic FluidHatch 8/64/576 ladder.
	protected override void OnDefinitionBound() => Configure();

	protected override NotifiableFluidTank CreateTank(int initialCapacity, int slots)
	{
		var tank = base.CreateTank(initialCapacity, slots);
		tank.SetFilter(stack => !stack.IsEmpty && stack.Type!.Id == FluidRegistry.Water.Id);
		return tank;
	}
}
