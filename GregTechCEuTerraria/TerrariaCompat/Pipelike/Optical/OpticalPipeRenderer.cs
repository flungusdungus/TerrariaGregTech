#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;
using Terraria.GameContent;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Optical;

// Minimal optical-pipe drawer. Mirror of LaserPipeRenderer - draws a thin
// straight-line glyph per cell, cyan when idle, brighter when active (data /
// computation flowed in the last 100 ticks). Optical pipes only connect along
// one axis, so no atlas/connection-mask machinery is needed.
public static class OpticalPipeRenderer
{
	private const int    LineThickness = 4;
	private static readonly Color IdleColor   = new( 64, 200, 200);  // cyan-ish
	private static readonly Color ActiveColor = new(128, 255, 255);  // brighter

	public static void DrawOpticalPipes()
	{
		if (OpticalPipeLayerSystem.Pipes.Count == 0) return;
		DrawLayer(Main.spriteBatch, foreground: false);
	}

	public static void DrawOpticalForegroundOverlay()
	{
		if (OpticalPipeLayerSystem.Pipes.Count == 0) return;
		var sb = Main.spriteBatch;
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
			DepthStencilState.None, RasterizerState.CullCounterClockwise, null,
			Main.GameViewMatrix.TransformationMatrix);
		try { DrawLayer(sb, foreground: true); }
		finally { sb.End(); }
	}

	private static void DrawLayer(SpriteBatch sb, bool foreground)
	{
		var layer = OpticalPipeLayerSystem.Pipes;
		int firstX = (int)(Main.screenPosition.X / 16) - 1;
		int lastX  = (int)((Main.screenPosition.X + Main.screenWidth) / 16) + 1;
		int firstY = (int)(Main.screenPosition.Y / 16) - 1;
		int lastY  = (int)((Main.screenPosition.Y + Main.screenHeight) / 16) + 1;

		var pixel = TextureAssets.MagicPixel.Value;

		foreach (var kv in layer.All)
		{
			int x = kv.Key.x;
			int y = kv.Key.y;
			if (x < firstX || x > lastX || y < firstY || y > lastY) continue;

			int mask = layer.ConnectionMask(x, y);  // N=1, S=2, W=4, E=8 (pipe links)
			// Also draw an arm toward an adjacent endpoint (computation / data
			// hatch) so a pipe touching a hatch shows the connection. Gated so it
			// only appears where the pipe could actually connect (<=2 total).
			mask |= EndpointMask(x, y, kv.Value.Open);
			bool active = OpticalPipeLayerSystem.IsActive(x, y);
			var tint = foreground ? Color.Lerp(active ? ActiveColor : IdleColor, Color.White, 0.25f)
			                      : (active ? ActiveColor : IdleColor);
			if (foreground) tint *= 0.55f;

			int ox = x * 16 - (int)Main.screenPosition.X;
			int oy = y * 16 - (int)Main.screenPosition.Y;

			// Per-side arms + a centre hub: isolated cell (mask 0) draws just the
			// hub (a dot); one connection = a stub; two perpendicular = an
			// L-corner (turn); two opposite = a straight run.
			int t = LineThickness;
			int o = (16 - t) / 2;            // o=6, t=4 -> hub spans [6,10]
			sb.Draw(pixel, new Rectangle(ox + o, oy + o, t, t), tint);                            // hub / dot
			if ((mask & 1) != 0) sb.Draw(pixel, new Rectangle(ox + o,     oy,         t, o), tint); // up
			if ((mask & 2) != 0) sb.Draw(pixel, new Rectangle(ox + o,     oy + o + t, t, o), tint); // down
			if ((mask & 4) != 0) sb.Draw(pixel, new Rectangle(ox,         oy + o,     o, t), tint); // left
			if ((mask & 8) != 0) sb.Draw(pixel, new Rectangle(ox + o + t, oy + o,     o, t), tint); // right
		}
	}

	// Bits for sides that have an adjacent computation/data endpoint AND where
	// the pipe still has room to connect (<=2 total). Lets a lone pipe next to a
	// hatch render its arm without faking a 3rd connection on a full pipe.
	private static int EndpointMask(int x, int y, byte open)
	{
		int m = 0;
		foreach (var (side, dx, dy) in OpticalConn.Sides)
		{
			int bit = OpticalConn.Bit(side);
			if ((open & bit) != 0) continue;                 // already a pipe link here
			if (OpticalConn.PopCount(open) >= 2) continue;   // full
			int hx = x + dx, hy = y + dy;
			bool endpoint =
				WorldCapability.Get<IOpticalComputationProvider>(hx, hy) != null ||
				WorldCapability.Get<IDataAccessHatch>(hx, hy) != null;
			if (endpoint) m |= bit;
		}
		return m;
	}
}
