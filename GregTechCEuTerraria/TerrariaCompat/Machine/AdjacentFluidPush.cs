#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Pipes-less fluid coupling. Pushes from a machine's output tank into any
// IFluidHandler on a tile bordering its footprint. Low cadence (every 5-10
// ticks) to keep cost ~O(perimeter).
//
// side=None scans full perimeter; exclude drops one cardinal (upstream
// SteamBoilerMachine.autoOutput skips DOWN - we're facing-less so a boiler
// passes exclude: Down).
public static class AdjacentFluidPush
{
	public static int Push(MetaMachine source, int sourceTankStart, int sourceTankCount, int maxAmount = 1000, IODirection side = IODirection.None, IODirection exclude = IODirection.None)
	{
		if (source is not IFluidHandler outHandler) return 0;
		return Push(source, outHandler, sourceTankStart, sourceTankCount, maxAmount, side, exclude);
	}

	// Explicit-handler overload for proxy/multi-tank parts; see AdjacentItemPush.
	public static int Push(MetaMachine source, IFluidHandler outHandler,
		int sourceTankStart, int sourceTankCount,
		int maxAmount = 1000, IODirection side = IODirection.None,
		IODirection exclude = IODirection.None)
	{
		if (maxAmount <= 0) return 0;

		int transferred = 0;

		for (int t = sourceTankStart; t < sourceTankStart + sourceTankCount && transferred < maxAmount; t++)
		{
			var stack = outHandler.GetTank(t);
			if (stack.IsEmpty) continue;

			int budget = System.Math.Min(stack.Amount, maxAmount - transferred);
			foreach (var (x, y, srcSide) in EnumerateAdjacentCells(source, side, exclude))
			{
				if (budget <= 0) break;
				if (!source.GetFluidCapFilter(srcSide, IO.OUT)(new FluidStack(stack.Type!, budget)))
					continue;
				var dest = WorldCapability.FluidHandlerAt(x, y, srcSide.Opposite());
				if (dest is null || ReferenceEquals(dest, outHandler)) continue;

				var probe = new FluidStack(stack.Type!, budget);
				int accepted = dest.Fill(probe, simulate: true);
				if (accepted <= 0) continue;
				dest.Fill(new FluidStack(stack.Type!, accepted), simulate: false);
				outHandler.Drain(new FluidStack(stack.Type!, accepted), simulate: false);
				transferred += accepted;
				budget -= accepted;
				stack = outHandler.GetTank(t);
				if (stack.IsEmpty) break;
			}
		}
		return transferred;
	}

	// Yields (x, y, side) for tiles bordering the machine's footprint.
	// Multi-cell neighbours are yielded once per cell touched - harmless since
	// item push breaks on first accepting cell and fluid push shares a budget.
	internal static System.Collections.Generic.IEnumerable<(int x, int y, IODirection side)> EnumerateAdjacentCells(MetaMachine source, IODirection side = IODirection.None, IODirection exclude = IODirection.None)
	{
		var (w, h) = source.Size;
		int x0 = source.Position.X;
		int y0 = source.Position.Y;

		switch (side)
		{
			case IODirection.Up:
				for (int dx = 0; dx < w; dx++) yield return (x0 + dx, y0 - 1, IODirection.Up);
				break;
			case IODirection.Down:
				for (int dx = 0; dx < w; dx++) yield return (x0 + dx, y0 + h, IODirection.Down);
				break;
			case IODirection.Left:
				for (int dy = 0; dy < h; dy++) yield return (x0 - 1, y0 + dy, IODirection.Left);
				break;
			case IODirection.Right:
				for (int dy = 0; dy < h; dy++) yield return (x0 + w, y0 + dy, IODirection.Right);
				break;
			default:
				if (exclude != IODirection.Up)
					for (int dx = -1; dx <= w; dx++) yield return (x0 + dx, y0 - 1, IODirection.Up);
				if (exclude != IODirection.Down)
					for (int dx = -1; dx <= w; dx++) yield return (x0 + dx, y0 + h, IODirection.Down);
				if (exclude != IODirection.Left)
					for (int dy = 0; dy < h; dy++) yield return (x0 - 1, y0 + dy, IODirection.Left);
				if (exclude != IODirection.Right)
					for (int dy = 0; dy < h; dy++) yield return (x0 + w, y0 + dy, IODirection.Right);
				break;
		}
	}
}
