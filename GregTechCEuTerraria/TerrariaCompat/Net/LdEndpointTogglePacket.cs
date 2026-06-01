#nullable enable
using System.IO;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.LongDistance;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Net;

// Server-authoritative LD-endpoint screwdriver flip (IN <-> OUT). No GUI viewers,
// so re-sync is via TileEntitySharing instead of periodic StateSync - same shape
// as TransformerTogglePacket.
public static class LdEndpointTogglePacket
{
	public static void SendRequest(int x, int y)
	{
		var p = NetRouter.NewPacket(PacketType.LdEndpointToggle);
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
			NetHelpers.LogBadPacket("LdEndpointToggle", "received on non-server side");
			return;
		}
		if (TileEntity.ByPosition.TryGetValue(new Point16(x, y), out var te)
		    && te is LongDistanceEndpointMachine ep)
			Apply(ep);
		else
			NetHelpers.LogBadPacket("LdEndpointToggle", $"no endpoint at ({x},{y}) from player {whoAmI}");
	}

	public static void Apply(LongDistanceEndpointMachine ep)
	{
		// First press from Unset -> Input; thereafter toggle Input <-> Output.
		ep.IoType = ep.IoType == IO.IN ? IO.OUT : IO.IN;
		// Topology unchanged but the active pair may flip - force every endpoint
		// to re-resolve its link.
		LongDistanceEndpointRegistry.InvalidateAll();
		if (Main.netMode == NetmodeID.Server)
			NetMessage.SendData(MessageID.TileEntitySharing, -1, -1, null,
				ep.ID, ep.Position.X, ep.Position.Y);
	}
}
