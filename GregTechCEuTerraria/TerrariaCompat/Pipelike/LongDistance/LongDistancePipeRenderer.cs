#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.LongDistance;

// Minimal LD-pipe drawer. Per-arm hub + stubs (same approach as the laser
// renderer; handles bends/branches/crosses correctly without an atlas). Colored
// per cell Type: item = amber, fluid = cyan - so the two networks read apart at a
// glance even where they cross (they never link - GridLayer.Connects gates on
// Type, so no arm is drawn between an item cell and a fluid cell).
public static class LongDistancePipeRenderer
{
	private const int LineThickness = 5;
	private static readonly Color ItemColor  = new(235, 165,  55);  // amber
	private static readonly Color FluidColor = new( 70, 175, 235);  // cyan

	public static void DrawPipes()
	{
		if (LongDistancePipeLayerSystem.Pipes.Count == 0) return;
		DrawLayer(Main.spriteBatch, foreground: false);
	}

	public static void DrawForegroundOverlay()
	{
		if (LongDistancePipeLayerSystem.Pipes.Count == 0) return;
		var sb = Main.spriteBatch;
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
			DepthStencilState.None, RasterizerState.CullCounterClockwise, null,
			Main.GameViewMatrix.TransformationMatrix);
		try { DrawLayer(sb, foreground: true); }
		finally { sb.End(); }
	}

	private static void DrawLayer(SpriteBatch sb, bool foreground)
	{
		var layer = LongDistancePipeLayerSystem.Pipes;
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

			int mask = layer.ConnectionMask(x, y);  // N=1, S=2, W=4, E=8 (same-type links)
			mask |= EndpointMask(x, y, kv.Value.Type);  // arm toward an adjacent endpoint
			var baseColor = kv.Value.Type == LongDistancePipeType.Fluid ? FluidColor : ItemColor;
			var tint = foreground ? Color.Lerp(baseColor, Color.White, 0.25f) * 0.55f : baseColor;

			int ox = x * 16 - (int)Main.screenPosition.X;
			int oy = y * 16 - (int)Main.screenPosition.Y;

			int t = LineThickness;
			int o = (16 - t) / 2;
			sb.Draw(pixel, new Rectangle(ox + o, oy + o, t, t), tint);                              // hub
			if ((mask & 1) != 0) sb.Draw(pixel, new Rectangle(ox + o,     oy,         t, o), tint);  // up
			if ((mask & 2) != 0) sb.Draw(pixel, new Rectangle(ox + o,     oy + o + t, t, o), tint);  // down
			if ((mask & 4) != 0) sb.Draw(pixel, new Rectangle(ox,         oy + o,     o, t), tint);  // left
			if ((mask & 8) != 0) sb.Draw(pixel, new Rectangle(ox + o + t, oy + o,     o, t), tint);  // right
		}
	}

	// Bits (N=1, S=2, W=4, E=8) for sides where an LD endpoint machine of the
	// same PipeType abuts this pipe cell - so the line visibly latches onto the
	// endpoint block instead of stopping a tile short.
	private static int EndpointMask(int x, int y, LongDistancePipeType type)
	{
		int m = 0;
		if (IsEndpoint(x, y - 1, type)) m |= 1;
		if (IsEndpoint(x, y + 1, type)) m |= 2;
		if (IsEndpoint(x - 1, y, type)) m |= 4;
		if (IsEndpoint(x + 1, y, type)) m |= 8;
		return m;
	}

	private static bool IsEndpoint(int x, int y, LongDistancePipeType type) =>
		Machine.MachineCellResolver.TryFindMachineAt(x, y, out var m)
		&& m is LongDistanceEndpointMachine ep && ep.PipeType == type;
}
