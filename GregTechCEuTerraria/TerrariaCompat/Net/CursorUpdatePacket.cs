#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Net.Actions;
using Terraria;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Net;

// Authoritative cursor (Main.mouseItem) result after a server-side
// SlotAction. Mirrors MagicStorage - clients never claim items the server
// didn't hand them. Delivery picks between cursor and player inventory.
public static class CursorUpdatePacket
{
	public enum Delivery : byte
	{
		Cursor          = 0,
		PlayerInventory = 1,
	}

	public static void SendTo(int toClient, Item item, Delivery delivery)
	{
		if (Main.netMode != NetmodeID.Server) return;
		var p = NetRouter.NewPacket(PacketType.CursorUpdate);
		p.Write((byte)delivery);
		p.WriteItem(item);
		p.Send(toClient: toClient);
	}

	public static void HandleOnClient(BinaryReader r)
	{
		if (Main.netMode != NetmodeID.MultiplayerClient) return;
		var delivery = (Delivery)r.ReadByte();
		var item = r.ReadItem();
		switch (delivery)
		{
			case Delivery.Cursor:
				Main.mouseItem = item;
				break;
			case Delivery.PlayerInventory:
				if (item.IsAir) return;
				// Auto-stacks into inventory; leftover drops on the ground.
				var leftover = Main.LocalPlayer.GetItem(
					Main.myPlayer, item,
					Terraria.GetItemSettings.InventoryEntityToPlayerInventorySettings);
				if (!leftover.IsAir && leftover.stack > 0)
					Item.NewItem(new Terraria.DataStructures.EntitySource_Misc("gtceu_cursor_overflow"),
						Main.LocalPlayer.position, Main.LocalPlayer.width, Main.LocalPlayer.height,
						leftover.type, leftover.stack, false, leftover.prefix);
				break;
		}
	}
}
