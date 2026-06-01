#nullable enable
using GregTechCEuTerraria.Api.Pipenet;
using GregTechCEuTerraria.TerrariaCompat.Items.Pipes;
using GregTechCEuTerraria.TerrariaCompat.Net;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Fluid;

// Fluid-pipe place/cut/refund. Symmetric to ItemPipeLayerHandle.
public sealed class FluidPipeLayerHandle : IGridLayerHandle
{
	public static readonly FluidPipeLayerHandle Instance = new();
	private FluidPipeLayerHandle() { }

	public bool Has(int x, int y) => FluidPipeLayerSystem.Pipes.Has(x, y);

	public bool TryPlace(FluidPipeCell cell, int x, int y, Player placer)
	{
		var existing = FluidPipeLayerSystem.Pipes.CellAt(x, y);
		if (existing.HasValue && existing.Value.Equals(cell)) return false;
		if (existing.HasValue) RefundAt(placer, existing.Value);
		FluidPipeLayerSystem.Pipes.Set(x, y, cell);
		FluidPipeLayerSystem.EnsureSides(x, y);
		FluidPipeNetSystem.OnPipeAdded(x, y, cell);
		FluidPipeLayerSystem.EnsureState(x, y);
		PipePackets.SendPlacedFluid(x, y, cell);
		NotifyAdjacentCoversNeighborChanged(x, y);
		return true;
	}

	public bool CutAt(int x, int y, Player remover)
	{
		var existing = FluidPipeLayerSystem.Pipes.CellAt(x, y);
		if (existing is null) return false;
		FluidPipeLayerSystem.Pipes.Remove(x, y);
		FluidPipeLayerSystem.DropSides(x, y);
		FluidPipeNetSystem.OnPipeRemoved(x, y);
		PipePackets.SendRemove(x, y, PipeKind.Fluid);
		RefundAt(remover, existing.Value);
		NotifyAdjacentCoversNeighborChanged(x, y);
		return true;
	}

	internal static void NotifyAdjacentCoversNeighborChanged(int x, int y)
	{
		foreach (var (_, dx, dy) in Machine.MachineSides.Cardinal4)
		{
			int nx = x + dx, ny = y + dy;
			var pcv = FluidPipeLayerSystem.GetSides(nx, ny);
			if (pcv is null) continue;
			((Api.Cover.ICoverable)pcv).OnCoversNeighborChanged();
		}
	}

	// Simple pipes need the tML-name branch; see ItemPipeLayerHandle.RefundAt.
	private static void RefundAt(Player player, FluidPipeCell cell)
	{
		int type;
		if (cell.IsSimple)
		{
			if (!ModContent.GetInstance<GregTechCEuTerraria>().TryFind<ModItem>("simple_fluid_pipe", out var mi))
				return;
			type = mi.Type;
		}
		else
		{
			string id = cell.MaterialId + "_" + PipeSizes.Word(cell.Size) + "_fluid_pipe";
			int? t = PipeItemRegistry.Get(id);
			if (t is null) return;
			type = t.Value;
		}
		global::GregTechCEuTerraria.TerrariaCompat.Utils.PlayerGive.Give(player, player.GetSource_Misc("PipeRemove"), type, 1);
	}
}
