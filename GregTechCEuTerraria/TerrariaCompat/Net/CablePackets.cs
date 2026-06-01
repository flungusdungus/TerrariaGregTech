#nullable enable
using System.IO;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Cable;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Net;

// Cable-layer sync (place / remove / late-join). Client mutates locally +
// ships; server applies + echoes. Server-initiated burnouts go to ALL
// clients via SendRemoveBroadcast. Cable cells are immutable post-place so
// last-write-wins is safe.
public static class CablePackets
{
	private static void WriteCell(BinaryWriter w, CableCell cell)
	{
		w.Write(cell.MaterialId);
		w.Write(cell.WireSize);
		w.Write(cell.Insulated);
		w.Write((byte)cell.Voltage);
		w.Write(cell.BaseAmperage);
		w.Write(cell.LossPerAmp);
	}

	private static CableCell ReadCell(BinaryReader r)
	{
		string mat = r.ReadString();
		byte size  = r.ReadByte();
		bool ins   = r.ReadBoolean();
		byte volt  = r.ReadByte();
		int amps   = r.ReadInt32();
		int loss   = r.ReadInt32();
		if (volt > 14) volt = 0;
		return new CableCell(mat, size, ins, (VoltageTier)volt, amps, loss);
	}

	public static void SendSet(int x, int y, CableCell cell)
	{
		if (Main.netMode == NetmodeID.SinglePlayer) return;
		var p = NetRouter.NewPacket(PacketType.CableSet);
		p.Write((short)x);
		p.Write((short)y);
		WriteCell(p, cell);
		p.Send();
	}

	public static void HandleSet(BinaryReader r, int whoAmI)
	{
		int x = r.ReadInt16();
		int y = r.ReadInt16();
		var cell = ReadCell(r);

		if (Main.netMode == NetmodeID.Server)
		{
			CableLayerSystem.Cables.Set(x, y, cell);
			var p = NetRouter.NewPacket(PacketType.CableSet);
			p.Write((short)x);
			p.Write((short)y);
			WriteCell(p, cell);
			p.Send(ignoreClient: whoAmI);
			return;
		}
		if (Main.netMode == NetmodeID.MultiplayerClient)
			CableLayerSystem.Cables.Set(x, y, cell);
	}

	public static void SendRemove(int x, int y)
	{
		if (Main.netMode == NetmodeID.SinglePlayer) return;
		var p = NetRouter.NewPacket(PacketType.CableRemove);
		p.Write((short)x);
		p.Write((short)y);
		p.Send();
	}

	// Server-only burnout-removal broadcast (no client owns this).
	public static void SendRemoveBroadcast(int x, int y)
	{
		if (Main.netMode != NetmodeID.Server) return;
		var p = NetRouter.NewPacket(PacketType.CableRemove);
		p.Write((short)x);
		p.Write((short)y);
		p.Send();
	}

	public static void HandleRemove(BinaryReader r, int whoAmI)
	{
		int x = r.ReadInt16();
		int y = r.ReadInt16();

		if (Main.netMode == NetmodeID.Server)
		{
			CableLayerSystem.Cables.Remove(x, y);
			var p = NetRouter.NewPacket(PacketType.CableRemove);
			p.Write((short)x);
			p.Write((short)y);
			p.Send(ignoreClient: whoAmI);
			return;
		}
		if (Main.netMode == NetmodeID.MultiplayerClient)
			CableLayerSystem.Cables.Remove(x, y);
	}

	public static void SendLayerRequest()
	{
		if (Main.netMode != NetmodeID.MultiplayerClient) return;
		var p = NetRouter.NewPacket(PacketType.CableLayerRequest);
		p.Send();
	}

	public static void HandleLayerRequest(BinaryReader r, int whoAmI)
	{
		if (Main.netMode != NetmodeID.Server) return;
		var p = NetRouter.NewPacket(PacketType.CableLayerFull);
		int n = CableLayerSystem.Cables.Count;
		p.Write(n);
		foreach (var kv in CableLayerSystem.Cables.All)
		{
			p.Write((short)kv.Key.x);
			p.Write((short)kv.Key.y);
			WriteCell(p, kv.Value);
		}
		p.Send(toClient: whoAmI);
	}

	public static void HandleLayerFull(BinaryReader r)
	{
		if (Main.netMode != NetmodeID.MultiplayerClient) return;
		CableLayerSystem.Cables.Clear();
		int n = r.ReadInt32();
		for (int i = 0; i < n; i++)
		{
			int x = r.ReadInt16();
			int y = r.ReadInt16();
			var cell = ReadCell(r);
			CableLayerSystem.Cables.Set(x, y, cell);
		}
	}
}
