#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Transfer;
using GregTechCEuTerraria.Common.Energy;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

// Port of ReservoirHatchPartMachine. EV-tier IN fluid hatch with a self-
// refilling 2-billion-mB water reservoir. Externally drainable but not
// fillable (source, not sink). 20-tick refill cadence. Pump multi's
// always-on water source.
public class ReservoirHatchPartMachine : FluidHatchPartMachine
{
	public const int FLUID_AMOUNT = 2_000_000_000;

	protected override string Label => "Reservoir Hatch";

	private InfiniteWaterTank? _waterTank;

	public ReservoirHatchPartMachine() : base() { }

	public void Configure() => Configure(
		tier: (int)VoltageTier.EV,
		io: IO.IN,
		initialCapacity: FLUID_AMOUNT,
		slots: 1);

	// Wrap our InfiniteWaterTank; trait config keeps capabilityIO=BOTH.
	protected override NotifiableFluidTank CreateTank(int initialCapacity, int slots)
	{
		_waterTank = new InfiniteWaterTank(initialCapacity);
		return new NotifiableFluidTank(new List<CustomFluidTank> { _waterTank }, Io, IO.BOTH);
	}

	protected override void AutoIOTick()
	{
		if (GetOffsetTimer() % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(20) != 0) return;
		if (!WorkingEnabled || _waterTank == null) return;
		if (!_waterTank.IsFull) _waterTank.RefillWater();
	}

	// Re-seeded full on load - persistence is unnecessary.
	private sealed class InfiniteWaterTank : CustomFluidTank
	{
		public InfiniteWaterTank(int capacity) : base(capacity)
		{
			SetFluid(new FluidStack(FluidRegistry.Water, capacity));
		}

		public bool IsFull => Fluid.Amount >= Capacity;

		// Bypass Fill rejection.
		public void RefillWater() => SetFluid(new FluidStack(FluidRegistry.Water, Capacity));

		public override int Fill(FluidStack resource, bool simulate) => 0;

		public new TagCompound SerializeNBT() => new();
		public new void DeserializeNBT(TagCompound tag) { }
	}
}
