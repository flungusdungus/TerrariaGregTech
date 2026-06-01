#nullable enable
using GregTechCEuTerraria.Api.Pipenet;
using GregTechCEuTerraria.TerrariaCompat.Items.Pipes;
using GregTechCEuTerraria.TerrariaCompat.Net;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.ItemPipe;

// Item-pipe place/cut/refund cycle. Singleton. PipeItem.BuildItemCell is
// the SSOT for material -> cell conversion; this handle just gates + sets +
// broadcasts + refunds.
public sealed class ItemPipeLayerHandle : IGridLayerHandle
{
	public static readonly ItemPipeLayerHandle Instance = new();
	private ItemPipeLayerHandle() { }

	public bool Has(int x, int y) => ItemPipeLayerSystem.Pipes.Has(x, y);

	public bool TryPlace(ItemPipeCell cell, int x, int y, Player placer)
	{
		var existing = ItemPipeLayerSystem.Pipes.CellAt(x, y);
		if (existing.HasValue && existing.Value.Equals(cell)) return false;
		if (existing.HasValue) RefundAt(placer, existing.Value);
		ItemPipeLayerSystem.Pipes.Set(x, y, cell);
		// PipeCoverable holds the cell's per-tick state + per-side covers.
		ItemPipeLayerSystem.EnsureSides(x, y);
		ItemPipeNetSystem.OnPipeAdded(x, y, cell);
		PipePackets.SendPlacedItem(x, y, cell);
		NotifyAdjacentCoversNeighborChanged(x, y);
		return true;
	}

	public bool CutAt(int x, int y, Player remover)
	{
		var existing = ItemPipeLayerSystem.Pipes.CellAt(x, y);
		if (existing is null) return false;
		ItemPipeLayerSystem.Pipes.Remove(x, y);
		ItemPipeLayerSystem.DropSides(x, y);
		ItemPipeNetSystem.OnPipeRemoved(x, y);
		PipePackets.SendRemove(x, y, PipeKind.Item);
		RefundAt(remover, existing.Value);
		NotifyAdjacentCoversNeighborChanged(x, y);
		return true;
	}

	// Re-evaluates adjacent pipe-side covers' subscriptions; required for an
	// Active side whose neighbour just toggled inventory <-> pipe. Internal +
	// static so the MP packet handlers can call it after their mutations.
	internal static void NotifyAdjacentCoversNeighborChanged(int x, int y)
	{
		foreach (var (_, dx, dy) in Machine.MachineSides.Cardinal4)
		{
			int nx = x + dx, ny = y + dy;
			var pcv = ItemPipeLayerSystem.GetSides(nx, ny);
			if (pcv is null) continue;
			((Api.Cover.ICoverable)pcv).OnCoversNeighborChanged();
		}
	}

	// Simple pipes live outside the dump-driven PipeItemRegistry, so they
	// need the tML-name lookup branch (the dump-id reconstruction would
	// produce a key like "simple_item_normal_item_pipe" with no entry).
	private static void RefundAt(Player player, ItemPipeCell cell)
	{
		int type;
		if (cell.IsSimple)
		{
			if (!ModContent.GetInstance<GregTechCEuTerraria>().TryFind<ModItem>("simple_item_pipe", out var mi))
				return;
			type = mi.Type;
		}
		else
		{
			string prefix = cell.Restrictive ? "_restrictive_item_pipe" : "_item_pipe";
			string id = cell.MaterialId + "_" + PipeSizes.Word(cell.Size) + prefix;
			int? t = PipeItemRegistry.Get(id);
			if (t is null) return;
			type = t.Value;
		}
		global::GregTechCEuTerraria.TerrariaCompat.Utils.PlayerGive.Give(player, player.GetSource_Misc("PipeRemove"), type, 1);
	}
}
