#nullable enable
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Net;

// Visual half of an Item Collector consuming dropped items in-place. The
// collection runs server-only (ItemCollectorMachine.OnTick is gated to the
// server), and Dust.NewDust no-ops on a dedicated server, so the sparkle has
// to be broadcast. Batched: one packet per machine per tick carries every
// (x, y) where an item was consumed this tick, mirroring the inline-PlayLocal
// + Send(on server) convention of BlockExplosionEffectPacket.
public static class ItemCollectEffectPacket
{
	public static void Send(IReadOnlyList<Point> tilePositions)
	{
		if (Main.netMode != NetmodeID.Server || tilePositions.Count == 0) return;
		var p = NetRouter.NewPacket(PacketType.ItemCollectEffect);
		int count = tilePositions.Count > byte.MaxValue ? byte.MaxValue : tilePositions.Count;
		p.Write((byte)count);
		for (int i = 0; i < count; i++)
		{
			p.Write((short)tilePositions[i].X);
			p.Write((short)tilePositions[i].Y);
		}
		p.Send();
	}

	public static void HandleOnClient(BinaryReader r)
	{
		int count = r.ReadByte();
		for (int i = 0; i < count; i++)
		{
			int x = r.ReadInt16();
			int y = r.ReadInt16();
			PlayLocal(x, y);
		}
	}

	// Called inline by the server-side collection path AND the remote-client
	// handler so the host sees their own collectors sparkle.
	public static void PlayLocal(int tileX, int tileY)
	{
		var world = new Vector2(tileX * 16f, tileY * 16f);
		for (int d = 0; d < 10; d++)
		{
			var dust = Dust.NewDustDirect(world, 16, 16, DustID.TreasureSparkle,
				SpeedX: 0f, SpeedY: 0f, Alpha: 100, newColor: default, Scale: 0.9f);
			dust.noGravity = true;
			dust.velocity *= 0.4f;
		}
	}
}
