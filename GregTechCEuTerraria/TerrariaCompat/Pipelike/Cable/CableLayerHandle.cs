#nullable enable
using GregTechCEuTerraria.Api.Pipenet;
using GregTechCEuTerraria.TerrariaCompat.Items.Cables;
using GregTechCEuTerraria.TerrariaCompat.Net;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Cable;

// Cable place/cut/refund. Sibling of *PipeLayerHandle. WireItem.BuildCell
// is the SSOT for material -> cell.
public sealed class CableLayerHandle : IGridLayerHandle
{
	public static readonly CableLayerHandle Instance = new();
	private CableLayerHandle() { }

	public bool Has(int x, int y) => CableLayerSystem.Cables.Has(x, y);

	public bool TryPlace(CableCell cell, int x, int y, Player placer)
	{
		var existing = CableLayerSystem.Cables.CellAt(x, y);
		if (existing.HasValue && existing.Value.Equals(cell)) return false;
		if (existing.HasValue) RefundCableAt(placer, existing.Value);
		CableLayerSystem.Cables.Set(x, y, cell);
		CablePackets.SendSet(x, y, cell);
		return true;
	}

	public bool CutAt(int x, int y, Player remover)
	{
		if (x <= 0 || x >= Main.maxTilesX - 1 || y <= 0 || y >= Main.maxTilesY - 1) return false;
		var existing = CableLayerSystem.Cables.CellAt(x, y);
		if (existing is not { } cell) return false;
		CableLayerSystem.Cables.Remove(x, y);
		CablePackets.SendRemove(x, y);
		RefundCableAt(remover, cell);
		SoundEngine.PlaySound(SoundID.Tink, new Vector2(x * 16f, y * 16f));
		return true;
	}

	private static void RefundCableAt(Player player, CableCell cell)
	{
		int? type = WireItemRegistry.Get(cell.MaterialId, cell.WireSize, cell.Insulated);
		if (type is null) return;
		global::GregTechCEuTerraria.TerrariaCompat.Utils.PlayerGive.Give(player, player.GetSource_ItemUse(player.HeldItem), type.Value, 1);
	}
}
