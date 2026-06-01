#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Net;

// Server-authoritative drum screwdriver-flip (toggles fluid auto-output).
// Port of DrumMachine.onScrewdriverClick - upstream toggles AutoOutputItems
// (apparent copy-paste quirk); we toggle the fluid side, the intended effect.
public static class DrumScrewdriverPacket
{
	public static void SendRequest(int x, int y)
	{
		var p = NetRouter.NewPacket(PacketType.DrumScrewdriver);
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
			NetHelpers.LogBadPacket("DrumScrewdriver", "received on non-server side");
			return;
		}
		if (TileEntity.ByPosition.TryGetValue(new Point16(x, y), out var te) && te is DrumMachine drum)
			Apply(drum, whoAmI);
		else
			NetHelpers.LogBadPacket("DrumScrewdriver", $"no drum at ({x},{y}) from player {whoAmI}");
	}

	public static void Apply(DrumMachine drum, int player)
	{
		drum.IsAutoOutput = !drum.IsAutoOutput;

		// Screwdriver-RMB isn't a viewer action; broadcast unconditionally.
		if (Main.netMode == NetmodeID.Server)
			NetMessage.SendData(MessageID.TileEntitySharing, -1, -1, null,
				drum.ID, drum.Position.X, drum.Position.Y);
	}
}
