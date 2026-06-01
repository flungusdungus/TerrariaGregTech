#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Common.Materials;
using GregTechCEuTerraria.Api.Data.Chemical.Material;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Cable;

// Renders cables. 64x64 per-size procedural atlas: 4x4 grid of 16x16 frames,
// frame = neighbour connection mask (N=1, S=2, W=4, E=8). Tint = material.
public static class CableRenderer
{
	private const int FrameSize = 16;
	private const int GridSide  = 4;
	private const int SheetSide = FrameSize * GridSide;

	// Per-size line thickness (must be even so the cable centres on the cell).
	private static readonly Dictionary<byte, int> _thicknessBySize = new()
	{
		{ 1,  2 },
		{ 2,  4 },
		{ 4,  6 },
		{ 8,  10 },
		{ 16, 14 },
	};
	private static readonly Dictionary<byte, Texture2D> _atlasCache = new();
	private const int DefaultThickness = 6;

	public static void DrawVisible() => DrawAll(Main.spriteBatch, foreground: false);

	// Re-render layer above tiles while holding a wire (analogue of vanilla
	// wires lighting up under wrench/cutter). Owns its SpriteBatch.
	public static void DrawForegroundOverlay()
	{
		var cables = CableLayerSystem.Cables;
		if (cables.Count == 0) return;

		var sb = Main.spriteBatch;
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
			DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);
		try { DrawAll(sb, foreground: true); }
		finally { sb.End(); }
	}

	private static void DrawAll(SpriteBatch sb, bool foreground)
	{
		var cables = CableLayerSystem.Cables;
		if (cables.Count == 0) return;

		int firstX = (int)(Main.screenPosition.X / 16) - 1;
		int lastX  = (int)((Main.screenPosition.X + Main.screenWidth) / 16) + 1;
		int firstY = (int)(Main.screenPosition.Y / 16) - 1;
		int lastY  = (int)((Main.screenPosition.Y + Main.screenHeight) / 16) + 1;

		// Wall pass renders in screen coords (no Main.offScreenRange offset);
		// SpriteBatch lifecycle is owned by caller (wall pass or overlay).
		foreach (var kv in cables.All)
		{
			int x = kv.Key.x;
			int y = kv.Key.y;
			if (x < firstX || x > lastX || y < firstY || y > lastY) continue;

			var atlas = AtlasFor(kv.Value.WireSize);
			if (atlas is null) continue;

			int frame = cables.ConnectionMask(x, y);
			int col = frame % 4;
			int row = frame / 4;
			var src = new Rectangle(col * FrameSize, row * FrameSize, FrameSize, FrameSize);

			Vector2 pos = new Vector2(
				x * 16 - (int)Main.screenPosition.X,
				y * 16 - (int)Main.screenPosition.Y);

			Color tint = MaterialColor(kv.Value.MaterialId);
			// Insulated cables render at half brightness ("rubber-jacketed").
			if (kv.Value.Insulated) tint = DarkenForInsulation(tint);

			// High-loss UX overlay - lerp toward red when this cable is past
			// the 50% loss threshold relative to its net's max producer
			// voltage. Loss percent climbs smoothly (0.5 -> 1.0 maps to
			// orange -> bright red), so the player can SEE long-run loss
			// instead of inferring it from the throughput numbers. Foreground
			// overlay (held wire/cutter) only - keeps the wall-pass color
			// material-honest for everyday visibility.
			if (foreground)
			{
				var net = TerrariaCompat.Pipelike.Cable.EnergyNetSystem.NetAt(x, y);
				if (net is not null)
				{
					float lossPct = net.GetCableLossPercent(x, y);
					if (lossPct >= 0.5f)
					{
						// Re-map [0.5, 1.0] -> [0, 1] for the lerp factor.
						float t = System.Math.Min(1f, (lossPct - 0.5f) * 2f);
						tint = Color.Lerp(tint, Color.Red, 0.4f + 0.5f * t);
					}
				}
			}

			if (!foreground)
			{
				Color light = Lighting.GetColor(x, y);
				tint = new Color(
					(byte)(tint.R * light.R / 255),
					(byte)(tint.G * light.G / 255),
					(byte)(tint.B * light.B / 255));
			}
			// Foreground overlay skips ambient modulation (vanilla wire-overlay parity).

			sb.Draw(atlas, pos, src, tint);
		}
	}

	// Resolves the per-size atlas via runtime procedural generation. Lazy:
	// first access for a given wire size builds the 64x64 white-on-transparent
	// junction-grid + uploads to GPU, then caches. Subsequent calls are pure
	// dictionary lookups.
	private static Texture2D AtlasFor(byte wireSize)
	{
		if (_atlasCache.TryGetValue(wireSize, out var cached)) return cached;
		int thickness = _thicknessBySize.TryGetValue(wireSize, out var t) ? t : DefaultThickness;
		var tex = BuildAtlas(thickness);
		_atlasCache[wireSize] = tex;
		return tex;
	}

	// Builds a 64x64 RGBA atlas: 4x4 grid of 16x16 frames. Frame index encodes
	// the neighbor mask (N=1, S=2, W=4, E=8 -> 0..15). Each frame draws a
	// centered junction square plus arms in the directions whose bits are set.
	// Pure white pixels - the per-cell material color tints at draw time.
	private static Texture2D BuildAtlas(int thickness)
	{
		var pixels = new Color[SheetSide * SheetSide];   // transparent
		int armLow  = (FrameSize - thickness) / 2;
		int armHigh = armLow + thickness - 1;

		for (int mask = 0; mask < GridSide * GridSide; mask++)
		{
			int col = mask % GridSide;
			int row = mask / GridSide;
			int ox  = col * FrameSize;
			int oy  = row * FrameSize;
			FillRect(pixels, ox + armLow, oy + armLow, ox + armHigh, oy + armHigh);
			if ((mask & 1) != 0) FillRect(pixels, ox + armLow, oy + 0,        ox + armHigh, oy + armLow);          // N
			if ((mask & 2) != 0) FillRect(pixels, ox + armLow, oy + armHigh,  ox + armHigh, oy + FrameSize - 1);    // S
			if ((mask & 4) != 0) FillRect(pixels, ox + 0,      oy + armLow,   ox + armLow,  oy + armHigh);          // W
			if ((mask & 8) != 0) FillRect(pixels, ox + armHigh, oy + armLow,  ox + FrameSize - 1, oy + armHigh);    // E
		}

		var tex = RuntimeTextureRegistry.New(SheetSide, SheetSide);
		tex.SetData(pixels);
		return tex;
	}

	private static void FillRect(Color[] pixels, int x0, int y0, int x1, int y1)
	{
		for (int y = y0; y <= y1; y++)
			for (int x = x0; x <= x1; x++)
				pixels[y * SheetSide + x] = Color.White;
	}

	// Half-RGB jacket look; also used by WireItem's held / dropped icon tint.
	public static Color DarkenForInsulation(Color c) =>
		new Color(c.R / 2, c.G / 2, c.B / 2, c.A);

	private static Color MaterialColor(string materialId)
	{
		if (!MaterialRegistry.All.TryGetValue(materialId, out var mat)) return Color.White;
		uint c = mat.Color ?? 0xFFFFFFu;
		return new Color((byte)((c >> 16) & 0xFF), (byte)((c >> 8) & 0xFF), (byte)(c & 0xFF));
	}
}
