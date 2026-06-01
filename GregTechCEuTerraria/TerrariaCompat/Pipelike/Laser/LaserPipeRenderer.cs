#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;
using Terraria.GameContent;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Laser;

// Minimal laser-pipe drawer. Draws a thin straight-line glyph per cell -
// magenta when idle, brighter when active (= LaserNetHandler.SetPipesActive
// fired in the last 100 ticks). Skips the atlas/connection-mask machinery
// the item/fluid renderer uses because laser pipes only ever connect along
// one axis (no T-junctions / corners / branches).
public static class LaserPipeRenderer
{
	// Visual constants.
	private const int    LineThickness = 4;                  // px in source space
	private static readonly Color IdleColor   = new(180,  64, 200);  // magenta-ish
	private static readonly Color ActiveColor = new(255, 128, 255);  // brighter

	public static void DrawLaserPipes()
	{
		var layer = LaserPipeLayerSystem.Pipes;
		if (layer.Count == 0) return;
		DrawLayer(Main.spriteBatch, foreground: false);
	}

	public static void DrawLaserForegroundOverlay()
	{
		var layer = LaserPipeLayerSystem.Pipes;
		if (layer.Count == 0) return;
		var sb = Main.spriteBatch;
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
			DepthStencilState.None, RasterizerState.CullCounterClockwise, null,
			Main.GameViewMatrix.TransformationMatrix);
		try { DrawLayer(sb, foreground: true); }
		finally { sb.End(); }
	}

	private static void DrawLayer(SpriteBatch sb, bool foreground)
	{
		var layer = LaserPipeLayerSystem.Pipes;
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
			// Also draw an arm toward an adjacent laser endpoint (hatch), gated
			// to the pipe's axis so a straight line into a hatch shows the link.
			mask |= EndpointMask(x, y, kv.Value.Open);
			bool active = LaserPipeLayerSystem.IsActive(x, y);
			var tint = foreground ? Color.Lerp(active ? ActiveColor : IdleColor, Color.White, 0.25f)
			                      : (active ? ActiveColor : IdleColor);
			if (foreground) tint *= 0.55f;  // dimmer over solid tiles

			int ox = x * 16 - (int)Main.screenPosition.X;
			int oy = y * 16 - (int)Main.screenPosition.Y;

			// Per-side arms + a centre hub: isolated cell (mask 0) draws just the
			// hub (a dot); one connection = a stub; opposite pair = a straight run.
			// (Laser is straight-only per the walker, but the per-arm draw also
			// renders any incidental corner correctly instead of a "+".)
			int t = LineThickness;
			int o = (16 - t) / 2;            // o=6, t=4 -> hub spans [6,10]
			sb.Draw(pixel, new Rectangle(ox + o, oy + o, t, t), tint);                            // hub / dot
			if ((mask & 1) != 0) sb.Draw(pixel, new Rectangle(ox + o,     oy,         t, o), tint); // up
			if ((mask & 2) != 0) sb.Draw(pixel, new Rectangle(ox + o,     oy + o + t, t, o), tint); // down
			if ((mask & 4) != 0) sb.Draw(pixel, new Rectangle(ox,         oy + o,     o, t), tint); // left
			if ((mask & 8) != 0) sb.Draw(pixel, new Rectangle(ox + o + t, oy + o,     o, t), tint); // right
		}
	}

	// Bits for sides with an adjacent laser endpoint (hatch) that lie on the
	// pipe's connection axis (straight-only) - so a laser line into a hatch
	// shows the link, but a perpendicular hatch on a straight pipe does not.
	private static int EndpointMask(int x, int y, byte open)
	{
		int m = 0;
		foreach (var (side, dx, dy) in LaserConn.Sides)
		{
			int bit = LaserConn.Bit(side);
			if ((open & bit) != 0) continue;                         // already a pipe link here
			int axis = bit | LaserConn.Bit(side.Opposite());
			if ((open & ~axis) != 0) continue;                       // off this pipe's axis
			int hx = x + dx, hy = y + dy;
			if (WorldCapability.Get<ILaserContainer>(hx, hy) != null)
				m |= bit;
		}
		return m;
	}
}
