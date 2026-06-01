#nullable enable
using System.IO;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.TerrariaCompat.Pipelike;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Fluid;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.ItemPipe;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Net;

// Pipe-side analogue of MachineStateSyncPacket. Wire layout:
//   byte         kind (PipeKind.Item / .Fluid)
//   short, short x, y
//   bool         hasCovers (false = drop the PipeCoverable client-side)
//   TagCompound  covers blob (only when hasCovers)
public static class PipeCoverSyncPacket
{
	public static void Broadcast(PipeKind layer, int x, int y) =>
		SendImpl(layer, x, y, toClient: -1);

	public static void SendTo(PipeKind layer, int x, int y, int toClient) =>
		SendImpl(layer, x, y, toClient);

	private static void SendImpl(PipeKind layer, int x, int y, int toClient)
	{
		if (Main.netMode != NetmodeID.Server) return;
		var p = NetRouter.NewPacket(PacketType.PipeCoverSync);
		p.Write((byte)layer);
		p.Write((short)x);
		p.Write((short)y);

		var pcv = layer == PipeKind.Fluid
			? FluidPipeLayerSystem.GetSides(x, y)
			: ItemPipeLayerSystem .GetSides(x, y);

		bool hasCovers = pcv is not null && ((ICoverable)pcv).HasAnyCover();
		p.Write(hasCovers);
		if (hasCovers)
		{
			var blob = new TagCompound();
			((ICoverable)pcv!).SaveCovers(blob);
			TagIO.Write(blob, p);
		}
		if (toClient >= 0) p.Send(toClient: toClient);
		else               p.Send();
	}

	public static void HandleOnClient(BinaryReader r)
	{
		if (Main.netMode != NetmodeID.MultiplayerClient) return;
		var layer = (PipeKind)r.ReadByte();
		int x = r.ReadInt16();
		int y = r.ReadInt16();
		bool hasCovers = r.ReadBoolean();

		if (!hasCovers)
		{
			if (layer == PipeKind.Fluid) FluidPipeLayerSystem.DropSides(x, y);
			else                          ItemPipeLayerSystem .DropSides(x, y);
			return;
		}

		var blob = TagIO.Read(r);
		var pcv = layer == PipeKind.Fluid
			? FluidPipeLayerSystem.EnsureSides(x, y)
			: ItemPipeLayerSystem .EnsureSides(x, y);
		// Blob is authoritative full state - reset before loading.
		for (int i = 0; i < CoverSides.Count; i++)
		{
			pcv._filterCovers[i]?.OnUnload();
			pcv._robotArms   [i]?.OnUnload();
		}
		System.Array.Clear(pcv._filterCovers, 0, pcv._filterCovers.Length);
		System.Array.Clear(pcv._robotArms,    0, pcv._robotArms.Length);
		((ICoverable)pcv).LoadCovers(blob);
	}
}
