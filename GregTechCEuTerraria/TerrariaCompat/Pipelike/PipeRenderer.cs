#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Common.Materials;
using GregTechCEuTerraria.Api.Pipenet;
using GregTechCEuTerraria.TerrariaCompat.Cover;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.ItemPipe;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Fluid;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike;

// Renders placed pipes (both layers). Hooked from each layer system's
// DoDraw_WallsAndBlacks detour - background z-band, so blocks placed on top
// cover the pipe (mirrors how cables sit below tiles).
//
// Per-size 64x64 procedural atlas: 4x4 grid of 16x16 frames, frame index =
// neighbour connection mask (N=1, S=2, W=4, E=8).
public static class PipeRenderer
{
	private const int FrameSize = 16;
	private const int GridSide  = 4;
	private const int SheetSide = FrameSize * GridSide;

	// Chunkier than cables so pipes read distinctly. Item/fluid share the
	// thickness - colour + restrictive overlay carry the distinction.
	private static readonly Dictionary<PipeSize, int> _thicknessBySize = new()
	{
		{ PipeSize.Tiny,       4  },
		{ PipeSize.Small,      6  },
		{ PipeSize.Normal,     8  },
		{ PipeSize.Large,      10 },
		{ PipeSize.Huge,       12 },
		{ PipeSize.Quadruple,  12 },
		{ PipeSize.Nonuple,    14 },
	};

	private static readonly Dictionary<PipeSize, Texture2D> _atlasCache = new();

	public static void DrawItemPipes()
	{
		var layer = ItemPipeLayerSystem.Pipes;
		if (layer.Count == 0) return;
		DrawLayer(Main.spriteBatch, layer, kind: PipeKind.Item, foreground: false);
	}

	public static void DrawFluidPipes()
	{
		var layer = FluidPipeLayerSystem.Pipes;
		if (layer.Count == 0) return;
		DrawLayer(Main.spriteBatch, layer, kind: PipeKind.Fluid, foreground: false);
	}

	// Per-kind overlay so pipe runs behind blocks stay traceable. Player only
	// sees the layer matching their held pipe item. Owns its SpriteBatch -
	// PostDrawTiles fires outside the wall pass.
	public static void DrawItemForegroundOverlay()
	{
		var layer = ItemPipeLayerSystem.Pipes;
		if (layer.Count == 0) return;
		DrawForegroundFor(layer, PipeKind.Item);
	}

	public static void DrawFluidForegroundOverlay()
	{
		var layer = FluidPipeLayerSystem.Pipes;
		if (layer.Count == 0) return;
		DrawForegroundFor(layer, PipeKind.Fluid);
	}

	private static void DrawForegroundFor<TCell>(GridLayer<TCell> layer, PipeKind kind) where TCell : struct
	{
		var sb = Main.spriteBatch;
		sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
			DepthStencilState.None, RasterizerState.CullCounterClockwise, null,
			Main.GameViewMatrix.TransformationMatrix);
		try { DrawLayer(sb, layer, kind, foreground: true); }
		finally { sb.End(); }
	}

	// Per-cell tint / size / restrictive flag from kind-specific accessor;
	// everything else is one shared path over GridLayer<TCell>.
	private static void DrawLayer<TCell>(SpriteBatch sb, GridLayer<TCell> layer, PipeKind kind, bool foreground)
		where TCell : struct
	{
		int firstX = (int)(Main.screenPosition.X / 16) - 1;
		int lastX  = (int)((Main.screenPosition.X + Main.screenWidth) / 16) + 1;
		int firstY = (int)(Main.screenPosition.Y / 16) - 1;
		int lastY  = (int)((Main.screenPosition.Y + Main.screenHeight) / 16) + 1;

		foreach (var kv in layer.All)
		{
			int x = kv.Key.x;
			int y = kv.Key.y;
			if (x < firstX || x > lastX || y < firstY || y > lastY) continue;

			(string materialId, PipeSize size, bool restrictive) = ReadCell(kv.Value, kind);
			var atlas = AtlasFor(size);
			if (atlas is null) continue;

			int frame = layer.ConnectionMask(x, y);
			int col = frame % GridSide;
			int row = frame / GridSide;
			var src = new Rectangle(col * FrameSize, row * FrameSize, FrameSize, FrameSize);

			Vector2 pos = new Vector2(
				x * 16 - (int)Main.screenPosition.X,
				y * 16 - (int)Main.screenPosition.Y);

			Color tint = MaterialColor(materialId);
			// Restrictive item pipes read darker - no separate texture set.
			if (restrictive) tint = Darken(tint, 0.55f);

			if (!foreground)
			{
				Color light = Lighting.GetColor(x, y);
				tint = new Color(
					(byte)(tint.R * light.R / 255),
					(byte)(tint.G * light.G / 255),
					(byte)(tint.B * light.B / 255));
			}
			// Foreground overlay skips ambient modulation (matches vanilla
			// wire-overlay full-brightness in dark caves).

			sb.Draw(atlas, pos, src, tint);

			// Per-side funnel endpoints on top of the pipe. Passive =
			// material colour, Orange = Active push
			// (pipe->inv), Blue = Active pull (inv->pipe). Only renders when
			// the side has an actual inventory neighbour.
			DrawSideFunnels(sb, x, y, kind, pos, MaterialColor(materialId));
		}
	}

	// === Per-side funnels =====================================================
	private static Texture2D? _funnelTriangleTex;
	private static Texture2D? _funnelSquareTex;

	// 8x8 triangle, tip at x=0 (pipe centre), base at x=W-1 (outer edge).
	// Drawn with origin=(0,4); FlipHorizontally swaps tip/base for IO.OUT.
	private static Texture2D? FunnelTriangle()
	{
		if (_funnelTriangleTex is not null) return _funnelTriangleTex;
		if (Main.graphics?.GraphicsDevice is null) return null;
		const int W = 8, H = 8;
		var tex = RuntimeTextureRegistry.New(W, H);
		var px = new Color[W * H];
		for (int x = 0; x < W; x++)
		{
			int half = (x * (H / 2) + (W / 2)) / W;
			int yLo = (H / 2) - half;
			int yHi = (H / 2) + half;
			for (int y = yLo; y <= yHi && y < H; y++)
				px[y * W + x] = Color.White;
		}
		tex.SetData(px);
		return _funnelTriangleTex = tex;
	}

	// Passive-mode marker (no flow direction). 8x8 to match triangle footprint.
	private static Texture2D? FunnelSquare()
	{
		if (_funnelSquareTex is not null) return _funnelSquareTex;
		if (Main.graphics?.GraphicsDevice is null) return null;
		const int W = 8, H = 8;
		var tex = RuntimeTextureRegistry.New(W, H);
		var px = new Color[W * H];
		// 6x6 centred; 1-px border keeps it from dominating the triangle widths.
		for (int x = 1; x < W - 1; x++)
			for (int y = 1; y < H - 1; y++)
				px[y * W + x] = Color.White;
		tex.SetData(px);
		return _funnelSquareTex = tex;
	}

	private static void DrawSideFunnels(SpriteBatch sb, int x, int y, PipeKind kind, Vector2 cellScreenPos, Color pipeMaterialColor)
	{
		var pcv = kind == PipeKind.Fluid
			? FluidPipeLayerSystem.GetSides(x, y)
			: ItemPipeLayerSystem .GetSides(x, y);
		if (pcv is null) return;

		var triTex    = FunnelTriangle();
		var squareTex = FunnelSquare();
		if (triTex is null || squareTex is null) return;

		Color light = Lighting.GetColor(x, y);
		bool busy = IsPipeBusy(x, y, kind);

		foreach (var side in CoverSides.All)
		{
			var mode = pcv.GetMode(side);
			if (mode == PipeSideMode.Off) continue;

			var probe = PipeNeighborProbe.ProbeAt(x, y, side, kind);
			if (probe != SideNeighbourKind.Inventory) continue;

			Color funnelColor = PickFunnelColor(pcv, side, mode, pipeMaterialColor);
			Color tintedColor = new Color(
				(byte)(funnelColor.R * light.R / 255),
				(byte)(funnelColor.G * light.G / 255),
				(byte)(funnelColor.B * light.B / 255));

			Vector2 pivot = cellScreenPos + new Vector2(8, 8);
			float rotation = side switch
			{
				CoverSide.Right => 0f,
				CoverSide.Down  => MathHelper.PiOver2,
				CoverSide.Left  => MathHelper.Pi,
				CoverSide.Up    => MathHelper.Pi + MathHelper.PiOver2,
				_               => 0f,
			};

			if (mode == PipeSideMode.Passive)
			{
				sb.Draw(squareTex, pivot, null, tintedColor, rotation,
					new Vector2(0, 4), 1f, SpriteEffects.None, 0f);
			}
			else
			{
				// IO.IN  = arrow inward (tip at pipe centre); texture as-is.
				// IO.OUT = arrow outward; FlipHorizontally swaps tip/base.
				bool extract = PipeCoverable.ActiveIoAt(pcv, side) == IO.OUT;
				var effects = extract ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
				sb.Draw(triTex, pivot, null, tintedColor, rotation,
					new Vector2(0, 4), 1f, effects, 0f);
			}

			// Trickle dust at the funnel base while the pipe is active.
			if (busy && Main.rand.NextFloat() < 0.10f)
				SpawnActivityDust(x, y, side, mode, pcv, funnelColor);
		}
	}

	private static bool IsPipeBusy(int x, int y, PipeKind kind)
	{
		// MP client reads the periodic stats cache; SP / server reads live state.
		if (kind == PipeKind.Fluid)
		{
			if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient)
			{
				if (!Fluid.FluidPipeLayerSystem.ClientTankSnapshots.TryGetValue((x, y), out var stacks)
				    || stacks is null) return false;
				foreach (var f in stacks) if (!f.IsEmpty) return true;
				return false;
			}
			var st = Fluid.FluidPipeLayerSystem.GetState(x, y);
			if (st is null) return false;
			foreach (var f in st.GetContainedFluids()) if (!f.IsEmpty) return true;
			return false;
		}

		if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient)
			return ItemPipeNetSystem.ClientTransferStats.TryGetValue((x, y), out int v) && v > 0;
		var ipcv = ItemPipeLayerSystem.GetSides(x, y);
		return ipcv is not null && ipcv.TransferredItems > 0;
	}

	private static void SpawnActivityDust(int x, int y, CoverSide side, PipeSideMode mode,
		PipeCoverable pcv, Color color)
	{
		// Spawn at the funnel base - one cell-edge step from pipe centre.
		float cx = x * 16 + 8;
		float cy = y * 16 + 8;
		var (dx, dy) = side switch
		{
			CoverSide.Up    => (0f, -8f),
			CoverSide.Down  => (0f, +8f),
			CoverSide.Left  => (-8f, 0f),
			CoverSide.Right => (+8f, 0f),
			_               => (0f, 0f),
		};
		Vector2 pos = new Vector2(cx + dx, cy + dy);

		// Velocity direction: push -> outward, pull -> inward, passive -> static jitter.
		float speed = 0.6f;
		Vector2 vel;
		if (mode == PipeSideMode.Active && PipeCoverable.ActiveIoAt(pcv, side) == IO.OUT)
			vel = new Vector2(dx, dy) / 8f * speed;   // outward
		else if (mode == PipeSideMode.Active)
			vel = new Vector2(-dx, -dy) / 8f * speed; // inward (pull / IO.IN)
		else
			vel = new Vector2(Main.rand.NextFloat(-0.2f, 0.2f), Main.rand.NextFloat(-0.2f, 0.2f));

		// Treasure-sparkle dust tinted with funnel colour - mode readable
		// even when the funnel itself is offscreen.
		int dust = Terraria.Dust.NewDust(pos - new Vector2(1, 1), 2, 2,
			Terraria.ID.DustID.TreasureSparkle, vel.X, vel.Y, 100, color, 0.9f);
		Terraria.Main.dust[dust].noGravity = true;
		Terraria.Main.dust[dust].fadeIn = 1.2f;
	}

	private static Color PickFunnelColor(PipeCoverable pcv, CoverSide side, PipeSideMode mode, Color pipeMaterialColor)
	{
		if (mode == PipeSideMode.Passive) return pipeMaterialColor;
		// IO.OUT = push (extract) = orange; IO.IN = pull (insert) = blue.
		bool push = PipeCoverable.ActiveIoAt(pcv, side) == IO.OUT;
		return push ? new Color(255, 140, 40)
		            : new Color( 80, 160, 255);
	}

	private static (string, PipeSize, bool) ReadCell<TCell>(TCell cell, PipeKind kind) where TCell : struct
	{
		if (kind == PipeKind.Item)
		{
			var c = (ItemPipeCell)(object)cell;
			return (c.MaterialId, c.Size, c.Restrictive);
		}
		else
		{
			var c = (FluidPipeCell)(object)cell;
			return (c.MaterialId, c.Size, false);
		}
	}

	private static Texture2D? AtlasFor(PipeSize size)
	{
		if (_atlasCache.TryGetValue(size, out var cached)) return cached;
		if (Main.graphics?.GraphicsDevice is null) return null;
		int thickness = _thicknessBySize.TryGetValue(size, out var t) ? t : 8;

		var tex = RuntimeTextureRegistry.New(SheetSide, SheetSide);
		var pixels = new Color[SheetSide * SheetSide];
		for (int frame = 0; frame < 16; frame++)
		{
			int col = frame % GridSide;
			int row = frame / GridSide;
			int ox = col * FrameSize;
			int oy = row * FrameSize;
			DrawFrame(pixels, SheetSide, ox, oy, frame, thickness);
		}
		tex.SetData(pixels);
		_atlasCache[size] = tex;
		return tex;
	}

	// 16x16 frame for connection mask (N=1, S=2, W=4, E=8). Centred hub +
	// per-arm band. mask == 0 draws just the hub so an isolated cell renders.
	private static void DrawFrame(Color[] dest, int stride, int ox, int oy, int mask, int thickness)
	{
		int half = thickness / 2;
		int center = FrameSize / 2;
		int xLo = center - half;
		int xHi = center + half - (thickness % 2 == 0 ? 1 : 0);

		FillRect(dest, stride, ox + xLo, oy + xLo, ox + xHi, oy + xHi, Color.White);

		if ((mask & 1) != 0) FillRect(dest, stride, ox + xLo, oy + 0,         ox + xHi, oy + xLo, Color.White); // N
		if ((mask & 2) != 0) FillRect(dest, stride, ox + xLo, oy + xHi,       ox + xHi, oy + 15,  Color.White); // S
		if ((mask & 4) != 0) FillRect(dest, stride, ox + 0,   oy + xLo,       ox + xLo, oy + xHi, Color.White); // W
		if ((mask & 8) != 0) FillRect(dest, stride, ox + xHi, oy + xLo,       ox + 15,  oy + xHi, Color.White); // E
	}

	private static void FillRect(Color[] dest, int stride, int x0, int y0, int x1, int y1, Color c)
	{
		for (int y = y0; y <= y1; y++)
		{
			for (int x = x0; x <= x1; x++)
			{
				dest[y * stride + x] = c;
			}
		}
	}

	private static Color MaterialColor(string materialId)
	{
		var mat = MaterialRegistry.Get(materialId);
		uint c = mat?.Color ?? 0xFFFFFFu;
		return new Color((byte)((c >> 16) & 0xFF), (byte)((c >> 8) & 0xFF), (byte)(c & 0xFF));
	}

	private static Color Darken(Color c, float factor) =>
		new Color((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

	public static void ClearAtlasCache()
	{
		foreach (var t in _atlasCache.Values) t?.Dispose();
		_atlasCache.Clear();
		_funnelTriangleTex?.Dispose();
		_funnelTriangleTex = null;
		_funnelSquareTex?.Dispose();
		_funnelSquareTex = null;
	}
}
