#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines.Transformers;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Net;

// Server-authoritative transformer screwdriver-flip. No GUI viewers, so
// re-sync is via TileEntitySharing instead of periodic StateSync.
public static class TransformerTogglePacket
{
	public static void SendRequest(int x, int y)
	{
		var p = NetRouter.NewPacket(PacketType.TransformerToggle);
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
			NetHelpers.LogBadPacket("TransformerToggle", "received on non-server side");
			return;
		}
		if (TileEntity.ByPosition.TryGetValue(new Point16(x, y), out var te)
		    && te is TransformerMachine tr)
			Apply(tr, whoAmI);
		else
			NetHelpers.LogBadPacket("TransformerToggle", $"no transformer at ({x},{y}) from player {whoAmI}");
	}

	public static void Apply(TransformerMachine tr, int player)
	{
		tr.SetTransformUp(!tr.IsTransformUp);
		if (Main.netMode == NetmodeID.Server)
			NetMessage.SendData(MessageID.TileEntitySharing, -1, -1, null,
				tr.ID, tr.Position.X, tr.Position.Y);
	}
}
