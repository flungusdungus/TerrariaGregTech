#nullable enable
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Net;

// Visual half of a machine self-destruct (boiler water-empty, over-voltage).
// Custom packet because SoundEngine.PlaySound / Dust.NewDust no-op on a
// dedicated server. The tile-kill half rides vanilla MessageID.TileManipulation.
public static class BlockExplosionEffectPacket
{
	public static void Send(int x, int y, int widthTiles, int heightTiles)
	{
		if (Main.netMode != NetmodeID.Server) return;
		var p = NetRouter.NewPacket(PacketType.BlockExplosionEffect);
		p.Write((short)x);
		p.Write((short)y);
		p.Write((byte)widthTiles);
		p.Write((byte)heightTiles);
		p.Send();
	}

	public static void HandleOnClient(BinaryReader r)
	{
		int x = r.ReadInt16();
		int y = r.ReadInt16();
		int w = r.ReadByte();
		int h = r.ReadByte();
		PlayLocal(x, y, w, h);
	}

	// Called inline by the server-side path AND the remote-client handler
	// so the host hears their own machines explode.
	public static void PlayLocal(int tileX, int tileY, int widthTiles, int heightTiles)
	{
		var world = new Vector2(tileX * 16f, tileY * 16f);
		SoundEngine.PlaySound(SoundID.Item62, world);
		int wPx = widthTiles  * 16;
		int hPx = heightTiles * 16;
		for (int d = 0; d < 12; d++)
			Dust.NewDust(world, wPx, hPx, DustID.Smoke,
				SpeedX: 0f, SpeedY: -1.5f, Alpha: 80, newColor: default, Scale: 1.6f);
	}
}
