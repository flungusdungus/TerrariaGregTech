#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Items.Registry;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Net;

// Server-authoritative crate-tape apply. Client asks; server consumes tape +
// re-syncs entity (TileEntitySharing) and hotbar (SyncEquipment).
public static class CrateTapePacket
{
	public static void SendRequest(int x, int y)
	{
		var p = NetRouter.NewPacket(PacketType.CrateTape);
		p.Write((short)x);
		p.Write((short)y);
		p.Send();
	}

	public static void Handle(BinaryReader r, int whoAmI)
	{
		int x = r.ReadInt16();
		int y = r.ReadInt16();
		if (Main.netMode != NetmodeID.Server)
		{
			NetHelpers.LogBadPacket("CrateTape", "received on non-server side");
			return;
		}
		if (TileEntity.ByPosition.TryGetValue(new Point16(x, y), out var te) && te is CrateMachine crate)
			Apply(crate, whoAmI);
		else
			NetHelpers.LogBadPacket("CrateTape", $"no crate at ({x},{y}) from player {whoAmI}");
	}

	// Server / SP path. MP clients route here via SendRequest.
	public static bool Apply(CrateMachine crate, int playerIndex)
	{
		if (crate.IsTaped) return false;
		if (playerIndex < 0 || playerIndex >= Main.maxPlayers) return false;
		var player = Main.player[playerIndex];
		var held = player.HeldItem;
		if (held is null || held.IsAir || !IsTape(held)) return false;

		crate.ApplyTape();
		held.stack--;
		if (held.stack <= 0) held.TurnToAir();

		// Tape-RMB isn't a viewer action; the periodic StateSync only ships
		// to viewers, so re-broadcast the entity unconditionally.
		if (Main.netMode == NetmodeID.Server)
		{
			NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null,
				player.whoAmI, player.selectedItem, held.prefix);
			NetMessage.SendData(MessageID.TileEntitySharing, -1, -1, null,
				crate.ID, crate.Position.X, crate.Position.Y);
		}
		return true;
	}

	public static bool IsTape(Item item)
	{
		if (item is null || item.IsAir) return false;
		return (RegistryItemLoader.TryGet("gtceu:duct_tape", out int dt) && item.type == dt)
		    || (RegistryItemLoader.TryGet("gtceu:basic_tape", out int bt) && item.type == bt);
	}
}
