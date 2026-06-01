#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Net;

// PlaceInWorld only fires on the placing client; the server creates the
// machine entity in response to this packet, applies portable data, then
// echoes via TileEntitySharing.
public static class MachinePlacedPacket
{
	public static void SendRequest(int x, int y, int tileType, TagCompound? portable)
	{
		var p = NetRouter.NewPacket(PacketType.MachinePlaced);
		p.Write((short)x);
		p.Write((short)y);
		p.Write(tileType);
		bool has = portable is { Count: > 0 };
		p.Write(has);
		if (has) TagIO.Write(portable!, p);
		p.Send();
	}

	public static void Handle(BinaryReader r, int whoAmI)
	{
		int x = r.ReadInt16();
		int y = r.ReadInt16();
		int tileType = r.ReadInt32();
		TagCompound? portable = r.ReadBoolean() ? TagIO.Read(r) : null;

		if (Main.netMode != NetmodeID.Server)
		{
			NetHelpers.LogBadPacket("MachinePlaced", "received on non-server side");
			return;
		}
		if (TileLoader.GetTile(tileType) is not IMetaMachineTile tile)
		{
			NetHelpers.LogBadPacket("MachinePlaced", $"tileType {tileType} is not a machine tile");
			return;
		}

		int id = tile.PlaceEntity(x, y);
		if (!TileEntity.ByID.TryGetValue(id, out var te) || te is not MetaMachine machine)
		{
			NetHelpers.LogBadPacket("MachinePlaced", $"PlaceEntity failed at ({x},{y})");
			return;
		}

		if (portable is { Count: > 0 })
			machine.ReadPortableData(portable);

		// TileEntitySharing runs NetSend/NetReceive on every client (incl. the
		// placer), which is what actually creates the entity client-side.
		NetMessage.SendData(MessageID.TileEntitySharing, -1, -1, null, id, x, y);
	}
}
