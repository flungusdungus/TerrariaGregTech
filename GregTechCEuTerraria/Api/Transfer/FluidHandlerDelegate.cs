#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Fluids;

namespace GregTechCEuTerraria.Api.Transfer;

// Port of com.gregtechceu.gtceu.api.transfer.fluid.FluidHandlerDelegate.
//
// An IFluidHandler that forwards every call to a wrapped `Inner` handler.
// Pump / fluid-filter cover capability wrappers subclass this and override
// Fill / Drain to gate transfer.
public class FluidHandlerDelegate : IFluidHandler
{
	public readonly IFluidHandler Inner;

	public FluidHandlerDelegate(IFluidHandler inner) => Inner = inner;

	public virtual int TankCount => Inner.TankCount;
	public virtual FluidStack GetTank(int tank) => Inner.GetTank(tank);
	public virtual int GetCapacity(int tank) => Inner.GetCapacity(tank);
	public virtual bool IsFluidValid(int tank, FluidStack fluid) => Inner.IsFluidValid(tank, fluid);
	public virtual int Fill(FluidStack fluid, bool simulate) => Inner.Fill(fluid, simulate);
	public virtual FluidStack Drain(int maxAmount, bool simulate) => Inner.Drain(maxAmount, simulate);
	public virtual FluidStack Drain(FluidStack fluidStack, bool simulate) => Inner.Drain(fluidStack, simulate);
	public virtual IFluidHandler GetTankAccess(int tank) => Inner.GetTankAccess(tank);
}
