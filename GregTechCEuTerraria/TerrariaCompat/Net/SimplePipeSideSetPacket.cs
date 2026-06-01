#nullable enable
using System.IO;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.TerrariaCompat.Pipelike;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Fluid;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.ItemPipe;
using Terraria;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Net;

// Set the 3-state simple-mode (Off / Insert / Extract) on a simple pipe.
// Server resolves the cover chain + broadcasts via PipeCoverSyncPacket.
public static class SimplePipeSideSetPacket
{
	public static void Send(PipeKind layer, int x, int y, CoverSide side, SimpleSideMode mode)
	{
		if (Main.netMode == NetmodeID.SinglePlayer)
		{
			ApplyServer(layer, x, y, side, mode);
			return;
		}
		var p = NetRouter.NewPacket(PacketType.SimplePipeSideSet);
		p.Write((byte)layer);
		p.Write((short)x);
		p.Write((short)y);
		p.Write((byte)side);
		p.Write((byte)mode);
		p.Send();
	}

	public static void HandleSet(BinaryReader r, int whoAmI)
	{
		if (Main.netMode != NetmodeID.Server)
		{
			NetHelpers.LogBadPacket("simple-pipe-side", "received on non-server side");
			return;
		}
		var layer = (PipeKind)r.ReadByte();
		int x = r.ReadInt16();
		int y = r.ReadInt16();
		var side = (CoverSide)r.ReadByte();
		var mode = (SimpleSideMode)r.ReadByte();
		ApplyServer(layer, x, y, side, mode);
	}

	private static void ApplyServer(PipeKind layer, int x, int y, CoverSide side, SimpleSideMode mode)
	{
		bool cellExists = layer == PipeKind.Fluid
			? FluidPipeLayerSystem.Pipes.Has(x, y)
			: ItemPipeLayerSystem .Pipes.Has(x, y);
		if (!cellExists) return;

		var pcv = layer == PipeKind.Fluid
			? FluidPipeLayerSystem.EnsureSides(x, y)
			: ItemPipeLayerSystem .EnsureSides(x, y);

		pcv.SetSimpleMode(side, mode);
		PipeCoverSyncPacket.Broadcast(layer, x, y);
	}
}
