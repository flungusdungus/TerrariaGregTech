#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Cable;
using Terraria;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Net;

// Per-network throughput broadcast keyed by AnchorCell (deterministic
// lex-min of the network's cells; both sides compute the same anchor).
// Empty packets still ship so client caches clear when a net goes idle.
public static class EnergyNetStatsPacket
{
	public static void Broadcast()
	{
		if (Main.netMode != NetmodeID.Server) return;
		var nets = EnergyNetSystem.Nets;

		int n = 0;
		for (int i = 0; i < nets.Count; i++)
		{
			if (nets[i].LastTickExtracted != 0 || nets[i].LastTickDelivered != 0) n++;
		}

		var p = NetRouter.NewPacket(PacketType.EnergyNetStats);
		p.Write(n);
		for (int i = 0; i < nets.Count; i++)
		{
			var net = nets[i];
			if (net.LastTickExtracted == 0 && net.LastTickDelivered == 0) continue;
			var anchor = net.AnchorCell;
			p.Write((short)anchor.x);
			p.Write((short)anchor.y);
			p.Write(net.LastTickExtracted);
			p.Write(net.LastTickDelivered);
		}
		p.Send();
	}

	public static void HandleOnClient(BinaryReader r)
	{
		if (Main.netMode != NetmodeID.MultiplayerClient) return;
		int n = r.ReadInt32();
		var cache = EnergyNetSystem.ClientStats;
		cache.Clear();
		for (int i = 0; i < n; i++)
		{
			int x = r.ReadInt16();
			int y = r.ReadInt16();
			long ex = r.ReadInt64();
			long de = r.ReadInt64();
			cache[(x, y)] = (ex, de);
		}
	}
}
