#nullable enable
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Fluids;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Fluid;

// Stands in for upstream's FluidPipeBlockEntity (we have no per-pipe BE).
// Keeps PipeTankList.cs line-for-line against upstream PipeTankList.java.
public interface IFluidPipeHost
{
	bool IsBlocked(CoverSide side);
	int CapacityPerTank { get; }
	void ReceivedFrom(CoverSide side);
	void CheckAndDestroy(FluidStack stack);
}
