#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Cable;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.TerrariaCompat.Capabilities;

// Forge-capability emulation narrowed to our 2-layer world. Centralizes the
// resolve every adjacency call site needs - notably multi-cell origin: a raw
// `TileEntity.ByPosition[(x,y)]` only hits the TOP-LEFT cell, so a cable on the
// bottom-right of a 2x2 machine would see no endpoint (MachineCellResolver fixes
// this). Cables aren't endpoints - their connectivity lives in CableLayerSystem.
public static class WorldCapability
{
	// Typed lookup at a cell, resolving multi-cell origin. T = any interface a
	// MetaMachine implements (IEnergyContainer / IItemHandler / IFluidHandler).
	public static T? Get<T>(int x, int y) where T : class
	{
		if (!MachineCellResolver.TryFindMachineAt(x, y, out var machine))
			return null;
		return machine as T;
	}

	// Side-aware resolve for a push ARRIVING on `arrivalSide` (AdjacentPush,
	// conveyor/pump covers, pipes). A machine routes through GetItemHandlerCap so
	// its covers + gated output side apply; a vanilla chest resolves directly.
	//
	// No side-less overload by design: it would hand back the RAW handler,
	// bypassing covers + IO gating (the bug that let a boiler push steam through
	// a fluid filter cover). Every automated resolve MUST pass the arrival side.
	public static IItemHandler? ItemHandlerAt(int x, int y, IODirection arrivalSide)
	{
		if (MachineCellResolver.TryFindMachineAt(x, y, out var machine))
			return machine.GetItemHandlerCap(arrivalSide);
		// Item-pipe layer: when an adjacent machine's auto-push arrives at a
		// pipe cell, hand back the pipe's ItemNetHandler so the routing
		// kicks in. The handler is cached per (pipe, side) on the
		// PipeCoverable so per-tick state (transferredItems / transferred
		// round-robin map) accumulates correctly across multiple inserts.
		if (TerrariaCompat.Pipelike.ItemPipe.ItemPipeLayerSystem.Pipes.Has(x, y))
		{
			var coverable = TerrariaCompat.Pipelike.ItemPipe.ItemPipeLayerSystem.EnsureSides(x, y);
			var side = ArrivalToCoverSide(arrivalSide);
			// Pipe-side mode `Off` means "not connected to the neighbour at
			// all" per the Terraria-side pipe spec - return null so external
			// auto-push paths can't route items in through an Off side, and
			// the cover's own GetOwnItemHandler returns null for Off too
			// (which makes ConveyorCover's `CanAttach` / `IsSubscriptionActive`
			// short-circuit and stops Active-cover ticking when the user
			// flips the side to Off).
			if (coverable.GetMode(side) == TerrariaCompat.Pipelike.PipeSideMode.Off)
				return null;
			int idx = (int)side;
			var cached = coverable.CachedItemHandlers[idx]
				as TerrariaCompat.Pipelike.ItemPipe.ItemNetHandler;
			if (cached is null)
			{
				// Network ref is refreshed inside Insert from the level
				// system, so passing the current net (possibly null) is OK.
				var net = TerrariaCompat.Pipelike.ItemPipe.ItemPipeNetSystem.Level
					.GetNetFromPos((x, y));
				if (net is not null)
				{
					cached = new TerrariaCompat.Pipelike.ItemPipe.ItemNetHandler(net, coverable, side);
					coverable.CachedItemHandlers[idx] = cached;
				}
			}
			// Upstream-parity: same fluid-pipe pattern - wrap the pipe's
			// external-facing handler with the side's cover so a side set to
			// Active+Push (ConveyorCover.Io == OUT) rejects external inserts
			// via the wrapper's `if (Io == OUT && ManualIOMode == Disabled)`
			// gate at ConveyorCover.CoverableItemHandlerWrapper. Without
			// this, a machine's auto-push routes items into the pipe through
			// an output-mode side and they get distributed back the wrong
			// direction - symmetric to the steam-into-water-pipe bug.
			IItemHandler? result = cached;
			if (result is not null && coverable.GetCoverAtSide(side) is { } cover)
				result = cover.GetItemHandlerCap(result);
			return result;
		}
		return Handlers.VanillaChestItemHandler.At(x, y);
	}

	// `arrivalSide` is already in the target's own coordinate frame - the
	// side of (x, y) facing the source - so the pipe's CoverSide matches
	// 1:1.
	private static Api.Cover.CoverSide ArrivalToCoverSide(IODirection arrival) => arrival switch
	{
		IODirection.Up    => Api.Cover.CoverSide.Up,
		IODirection.Down  => Api.Cover.CoverSide.Down,
		IODirection.Left  => Api.Cover.CoverSide.Left,
		IODirection.Right => Api.Cover.CoverSide.Right,
		_                 => Api.Cover.CoverSide.Up,
	};

	public static IFluidHandler? FluidHandlerAt(int x, int y, IODirection arrivalSide)
	{
		if (MachineCellResolver.TryFindMachineAt(x, y, out var machine))
			return machine.GetFluidHandlerCap(arrivalSide);
		// Fluid-pipe layer: when an adjacent machine's auto-push arrives at a
		// pipe cell, hand back the pipe's per-side PipeTankList so its tanks
		// fill / drain like a normal IFluidHandler. Direct structural mirror
		// of the item-pipe branch above. Mode `Off` blocks the side per the
		// project-wide pipe spec.
		if (TerrariaCompat.Pipelike.Fluid.FluidPipeLayerSystem.Pipes.Has(x, y))
		{
			var coverable = TerrariaCompat.Pipelike.Fluid.FluidPipeLayerSystem.EnsureSides(x, y);
			var side = ArrivalToCoverSide(arrivalSide);
			if (coverable.GetMode(side) == TerrariaCompat.Pipelike.PipeSideMode.Off)
				return null;
			var state = TerrariaCompat.Pipelike.Fluid.FluidPipeLayerSystem.EnsureState(x, y);
			IFluidHandler? result = state.GetTankList(side);
			// Upstream-parity: external resolves on a pipe with a cover must
			// be wrapped by the cover's GetFluidHandlerCap so the cover's
			// Fill/Drain gates fire. Without this, a side set to Active+Push
			// (PumpCover.Io == OUT) silently accepts external inserts because
			// the boiler's auto-push fills the raw PipeTankList directly,
			// bypassing the wrapper's `if (Io == OUT && ManualIOMode ==
			// Disabled) return 0;` gate at PumpCover.CoverableFluidHandlerWrapper.
			// FluidPipeState.DistributeFluid already wraps on the OWN
			// outbound path; this mirrors it on the incoming path. Same shape
			// MetaMachine.GetFluidHandlerCap uses.
			if (coverable.GetCoverAtSide(side) is { } cover)
				result = cover.GetFluidHandlerCap(result);
			return result;
		}
		return null;   // vanilla Terraria has no fluid-container tiles
	}

	// Map a cover's attach side to the outward IODirection used by Perimeter.
	public static IODirection ToIODirection(Api.Cover.CoverSide side) => side switch
	{
		Api.Cover.CoverSide.Up => IODirection.Up,
		Api.Cover.CoverSide.Down => IODirection.Down,
		Api.Cover.CoverSide.Left => IODirection.Left,
		Api.Cover.CoverSide.Right => IODirection.Right,
		_ => IODirection.None,
	};

	// Yields each perimeter cell adjacent to a machine footprint along with
	// the outward-facing IODirection from the machine's perspective. For a 2x2
	// at origin (3,5) (width=2, height=2), yields up to 8 cells:
	//   (3,4)/Up  (4,4)/Up
	//   (2,5)/Left  (2,6)/Left
	//   (5,5)/Right  (5,6)/Right
	//   (3,7)/Down  (4,7)/Down
	//
	// Corner cells are NOT yielded (cables can't be cardinal-adjacent through
	// a diagonal - matches upstream's 6-direction adjacency rule projected to
	// 4 sides).
	public static IEnumerable<(IODirection side, int x, int y)> Perimeter(
		int originX, int originY, int width, int height)
	{
		// Top edge (Up)
		for (int dx = 0; dx < width; dx++)
			yield return (IODirection.Up, originX + dx, originY - 1);
		// Bottom edge (Down) - Terraria Y grows downward, so "down" = origin+height
		for (int dx = 0; dx < width; dx++)
			yield return (IODirection.Down, originX + dx, originY + height);
		// Left edge
		for (int dy = 0; dy < height; dy++)
			yield return (IODirection.Left, originX - 1, originY + dy);
		// Right edge
		for (int dy = 0; dy < height; dy++)
			yield return (IODirection.Right, originX + width, originY + dy);
	}

	// Convenience overload - accepts a machine directly.
	public static IEnumerable<(IODirection side, int x, int y)> Perimeter(MetaMachine machine) =>
		Perimeter(machine.Position.X, machine.Position.Y, machine.Size.Width, machine.Size.Height);

	// For a cable at (cableX, cableY), return the IEnergyContainer endpoint
	// that sits at the SAME cell (the "wire behind machine" connectivity
	// model - cables live on the background CableLayer, machines on the
	// foreground tile grid; they can coexist at the same coord).
	//
	// Cardinal-adjacent endpoints are NOT yielded - wire-to-machine
	// connection requires the wire to be at the machine's footprint cell,
	// not next to it. (Wire-to-wire connection uses cardinal cells, but
	// that's the walker's `TryGetCellAt` against the CableLayer, not this.)
	//
	// Returns IODirection.None for the side argument because there's no
	// directional side for a same-cell connection - energy enters the
	// endpoint as "ambient/internal" delivery.
	public static (IODirection sideFromCable, IEnergyContainer ep)? CableEndpointAtCell(int cableX, int cableY)
	{
		var ep = Get<IEnergyContainer>(cableX, cableY);
		return ep is null ? null : (IODirection.None, ep);
	}

	// For a machine, enumerate every cable cell that sits at the machine's
	// own footprint cells. Inverse of CableEndpointAtCell. Cardinal-adjacent
	// cables are NOT yielded - only cables that share a cell with the
	// machine's footprint (wire behind machine).
	public static IEnumerable<(int x, int y, CableCell cable)>
		CablesAtFootprint(MetaMachine machine)
	{
		var layer = CableLayerSystem.Cables;
		int ox = machine.Position.X, oy = machine.Position.Y;
		var (w, h) = machine.Size;
		for (int dx = 0; dx < w; dx++)
		for (int dy = 0; dy < h; dy++)
		{
			var cell = layer.CellAt(ox + dx, oy + dy);
			if (cell is CableCell cc)
				yield return (ox + dx, oy + dy, cc);
		}
	}

	// Inverse of CableEndpointAtCell - for a machine, enumerate every
	// cable cell touching its footprint along with the cable cell and the
	// outward-facing side from the machine. Used by cable-render code to
	// know which sides of a machine have an attached cable.
	public static IEnumerable<(IODirection side, int x, int y, CableCell cable)>
		AdjacentCables(MetaMachine machine)
	{
		var layer = CableLayerSystem.Cables;
		foreach (var (side, x, y) in Perimeter(machine))
		{
			var cell = layer.CellAt(x, y);
			if (cell is CableCell cc)
				yield return (side, x, y, cc);
		}
	}
}
