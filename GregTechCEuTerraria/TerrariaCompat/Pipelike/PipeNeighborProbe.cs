#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Fluid;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.ItemPipe;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike;

// "From a pipe at (x, y), what's on each cardinal side?" Pipe = a CONNECTED
// same-net pipe; different-material pipes are two parallel runs, not Pipe.
public enum SideNeighbourKind : byte { None, Pipe, Inventory }

public static class PipeNeighborProbe
{
	public static SideNeighbourKind[] Probe(int x, int y, PipeKind layer)
	{
		var result = new SideNeighbourKind[CoverSides.Count];
		foreach (var side in CoverSides.All)
			result[(int)side] = ProbeAt(x, y, side, layer);
		return result;
	}

	public static SideNeighbourKind ProbeAt(int x, int y, CoverSide side, PipeKind layer)
	{
		var dir = ToIODirection(side);
		var (dx, dy) = dir.Offset();
		int nx = x + dx, ny = y + dy;

		if (IsConnectedPipe(x, y, nx, ny, layer)) return SideNeighbourKind.Pipe;

		var face = dir.Opposite();
		if (MachineCellResolver.TryFindMachineAt(nx, ny, out var machine))
		{
			bool ok = layer == PipeKind.Fluid
				? machine.GetFluidHandlerCap(face) != null
				: machine.GetItemHandlerCap (face) != null;
			return ok ? SideNeighbourKind.Inventory : SideNeighbourKind.None;
		}

		// Vanilla chest - item pipes only.
		if (layer != PipeKind.Fluid)
		{
			var handler = TerrariaCompat.Capabilities.Handlers.VanillaChestItemHandler.At(nx, ny);
			if (handler != null) return SideNeighbourKind.Inventory;
		}

		return SideNeighbourKind.None;
	}

	// Connected = same-net (items: marks match) / same-material (fluids).
	public static bool IsConnectedPipe(int x1, int y1, int x2, int y2, PipeKind layer)
	{
		if (layer == PipeKind.Fluid)
		{
			var a = FluidPipeLayerSystem.Pipes.CellAt(x1, y1);
			var b = FluidPipeLayerSystem.Pipes.CellAt(x2, y2);
			return a.HasValue && b.HasValue && a.Value.MaterialId == b.Value.MaterialId;
		}
		if (!ItemPipeLayerSystem.Pipes.Has(x1, y1) || !ItemPipeLayerSystem.Pipes.Has(x2, y2))
			return false;
		var netA = ItemPipeNetSystem.Level?.GetNetFromPos((x1, y1));
		var netB = ItemPipeNetSystem.Level?.GetNetFromPos((x2, y2));
		return netA != null && ReferenceEquals(netA, netB);
	}

	// Bool probes - true only when neighbour is an actual handler (Inventory).
	public static bool[] ProbeItem(int x, int y)  => ProbeBool(x, y, PipeKind.Item);
	public static bool[] ProbeFluid(int x, int y) => ProbeBool(x, y, PipeKind.Fluid);

	private static bool[] ProbeBool(int x, int y, PipeKind layer)
	{
		var live = new bool[CoverSides.Count];
		foreach (var side in CoverSides.All)
			live[(int)side] = ProbeAt(x, y, side, layer) == SideNeighbourKind.Inventory;
		return live;
	}

	public static bool HasAnyLive(int x, int y, PipeKind layer)
	{
		foreach (var side in CoverSides.All)
			if (ProbeAt(x, y, side, layer) == SideNeighbourKind.Inventory) return true;
		return false;
	}

	private static IODirection ToIODirection(CoverSide side) => side switch
	{
		CoverSide.Up    => IODirection.Up,
		CoverSide.Down  => IODirection.Down,
		CoverSide.Left  => IODirection.Left,
		CoverSide.Right => IODirection.Right,
		_               => IODirection.None,
	};
}
