#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.TerrariaCompat.Cover.Ender;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Net;

// Virtual ender channel contents. Channels live server-side in
// VirtualEnderRegistry and aren't machine state - clients mirror through
// this packet so the ender-cover popup reads channels uniformly in SP / MP.
public static class EnderChannelSyncPacket
{
	public static void SendChannelsTo(MetaMachine machine, int toClient)
	{
		if (Main.netMode != NetmodeID.Server) return;
		var seen = new HashSet<string>();
		foreach (CoverSide side in Enum.GetValues<CoverSide>())
		{
			if (machine.GetCoverAtSide(side) is not IEnderLinkCover ender) continue;
			if (ender.EntryType == EnderEntryType.Redstone) continue;   // no contents view
			if (!seen.Add((int)ender.EntryType + ender.ChannelName)) continue;
			var entry = VirtualEnderRegistry.Instance.GetEntry(ender.EntryType, ender.ChannelName);
			if (entry is not null)
				SendEntry(ender.EntryType, ender.ChannelName, entry, toClient);
		}
	}

	public static void Broadcast(MetaMachine machine)
	{
		if (Main.netMode != NetmodeID.Server) return;
		foreach (int viewer in machine.Viewers)
			SendChannelsTo(machine, viewer);
	}

	private static void SendEntry(EnderEntryType type, string name, VirtualEntry entry, int toClient)
	{
		var p = NetRouter.NewPacket(PacketType.EnderChannelSync);
		p.Write((byte)type);
		p.Write(name);
		var tag = new TagCompound();
		entry.Save(tag);
		TagIO.Write(tag, p);
		p.Send(toClient: toClient);
	}

	public static void HandleOnClient(BinaryReader r)
	{
		if (Main.netMode != NetmodeID.MultiplayerClient) return;
		var type = (EnderEntryType)r.ReadByte();
		string name = r.ReadString();
		var tag = TagIO.Read(r);
		var entry = VirtualEnderRegistry.Instance.GetOrCreateEntry(type, name);
		entry.Load(tag);
	}
}
