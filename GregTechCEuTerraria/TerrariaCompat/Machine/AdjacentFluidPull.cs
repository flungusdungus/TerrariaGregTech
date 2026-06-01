#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Pull side of AdjacentFluidPush - the symmetric helper. Walks the
// machine's footprint perimeter, drains the neighbour's IFluidHandler, and
// fills the source machine's tanks (subject to the source's per-side input
// cover filter). Used by multiblock fluid input hatches (`FluidHatchPart
// Machine` IO.IN / IO.BOTH branch). Same design notes as `AdjacentItemPull`.
public static class AdjacentFluidPull
{
	public static int Pull(MetaMachine source, int maxAmount = 1000,
		IODirection side = IODirection.None, IODirection exclude = IODirection.None)
	{
		if (source is not IFluidHandler ourHandler) return 0;
		if (maxAmount <= 0) return 0;
		int transferred = 0;

		foreach (var (x, y, srcSide) in AdjacentFluidPush.EnumerateAdjacentCells(source, side, exclude))
		{
			if (transferred >= maxAmount) break;

			// Per-side fluid filter (cover filter on this side, IO.IN).
			var ourFilter = source.GetFluidCapFilter(srcSide, IO.IN);

			// Resolve neighbour's handler - cover-aware.
			var srcHandler = WorldCapability.FluidHandlerAt(x, y, srcSide.Opposite());
			if (srcHandler is null || ReferenceEquals(srcHandler, ourHandler)) continue;

			int budget = maxAmount - transferred;
			// Probe what the neighbour offers without committing.
			var available = srcHandler.Drain(budget, simulate: true);
			if (available.IsEmpty || available.Amount <= 0) continue;
			if (!ourFilter(available)) continue;

			// Can we accept it?
			int accepted = ourHandler.Fill(available, simulate: true);
			if (accepted <= 0) continue;

			// Commit - drain EXACTLY `accepted` mB of the same fluid type.
			var actuallyDrained = srcHandler.Drain(new FluidStack(available.Type!, accepted), simulate: false);
			if (actuallyDrained.IsEmpty || actuallyDrained.Amount <= 0) continue;
			ourHandler.Fill(actuallyDrained, simulate: false);
			transferred += actuallyDrained.Amount;
		}
		return transferred;
	}
}
