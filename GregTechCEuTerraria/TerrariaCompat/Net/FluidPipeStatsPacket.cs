#nullable enable
using System.Collections.Generic;
using System.IO;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Fluid;
using Terraria;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Net;

// Periodic per-fluid-pipe tank-contents broadcast (every 6 ticks via
// FluidPipeNetSystem.PostUpdateWorld). Without this MP clients read zeros
// from FluidPipeState.GetContainedFluids - tanks only mutate server-side.
// Empty pipes are skipped.
public static class FluidPipeStatsPacket
{
	public static void Broadcast()
	{
		if (Main.netMode != NetmodeID.Server) return;

		int n = 0;
		foreach (var kv in FluidPipeLayerSystem.AllStates)
		{
			foreach (var f in kv.Value.GetContainedFluids())
				if (!f.IsEmpty) { n++; break; }
		}

		var p = NetRouter.NewPacket(PacketType.FluidPipeStats);
		p.Write(n);
		foreach (var kv in FluidPipeLayerSystem.AllStates)
		{
			var fluids = kv.Value.GetContainedFluids();
			bool any = false;
			foreach (var f in fluids) if (!f.IsEmpty) { any = true; break; }
			if (!any) continue;

			p.Write((short)kv.Key.x);
			p.Write((short)kv.Key.y);
			p.Write((byte)fluids.Length);
			foreach (var f in fluids)
			{
				if (f.IsEmpty || f.Type is null)
				{
					p.Write("");
					p.Write(0);
				}
				else
				{
					p.Write(f.Type.Id);
					p.Write(f.Amount);
				}
			}
		}
		p.Send();
	}

	public static void HandleOnClient(BinaryReader r)
	{
		if (Main.netMode != NetmodeID.MultiplayerClient) return;
		int n = r.ReadInt32();
		var cache = FluidPipeLayerSystem.ClientTankSnapshots;
		cache.Clear();
		for (int i = 0; i < n; i++)
		{
			int x = r.ReadInt16();
			int y = r.ReadInt16();
			int channels = r.ReadByte();
			var stacks = new FluidStack[channels];
			for (int c = 0; c < channels; c++)
			{
				string id = r.ReadString();
				int amount = r.ReadInt32();
				stacks[c] = (id.Length == 0 || amount <= 0 || !FluidRegistry.TryGet(id, out var ft))
					? FluidStack.Empty
					: new FluidStack(ft, amount);
			}
			cache[(x, y)] = stacks;
		}
	}
}
