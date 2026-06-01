#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;
using Terraria;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Net;

// Multiblock formed/unformed edge. Clients don't run AsyncCheckPattern
// (SystemTick is server-only), so without this IsFormed stays at whatever
// the initial TileEntitySharing blob carried and never updates.
public static class MultiblockFormedPacket
{
	public static void SendBroadcast(int controllerX, int controllerY, bool isFormed, bool isFlipped)
	{
		if (Main.netMode != NetmodeID.Server) return;
		var p = NetRouter.NewPacket(PacketType.MultiblockFormed);
		p.Write((short)controllerX);
		p.Write((short)controllerY);
		p.Write(isFormed);
		p.Write(isFlipped);
		p.Send();
	}

	public static void HandleSet(BinaryReader r)
	{
		short cx = r.ReadInt16();
		short cy = r.ReadInt16();
		bool isFormed = r.ReadBoolean();
		bool isFlipped = r.ReadBoolean();
		if (Main.netMode != NetmodeID.MultiplayerClient) return;

		if (MetaMachine.GetMachineAt(cx, cy) is MultiblockControllerMachine controller)
			controller.ApplyClientFormedSync(isFormed, isFlipped);
	}
}
