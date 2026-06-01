#nullable enable
using System;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.Common;

// Boss-agnostic texture-composite + wing-draw primitives. Used by per-boss
// renderers that bake a composite body from upstream PNGs at warm-up. All
// straight-alpha (tML PNGs aren't premultiplied), nearest-neighbour, and
// graphics-only - callers must guard Main.dedServ before invoking.
public static class BossArt
{
	// Load Content/Textures/<assetPath>.png and nearest-sample its first square
	// frame down to a `face`x`face` Color[] (handles 16px, HD, animated strips).
	public static Color[]? LoadFace(string assetPath, int face)
	{
		if (!ModContent.HasAsset(assetPath)) return null;
		Texture2D src;
		try { src = ModContent.Request<Texture2D>(assetPath, AssetRequestMode.ImmediateLoad).Value; }
		catch { return null; }

		int w = src.Width, h = src.Height;
		if (w <= 0 || h <= 0) return null;
		var raw = new Color[w * h];
		src.GetData(raw);

		int frame = Math.Min(w, h);
		var dst = new Color[face * face];
		for (int y = 0; y < face; y++)
			for (int x = 0; x < face; x++)
				dst[y * face + x] = raw[(y * frame / face) * w + (x * frame / face)];
		return dst;
	}

	// Straight-alpha src-over of one source pixel onto a destination pixel.
	public static void Over(ref Color d, Color s)
	{
		if (s.A == 0) return;
		if (s.A == 255) { d = s; return; }
		float sa = s.A / 255f, da = d.A / 255f, ia = 1f - sa, ao = sa + da * ia;
		if (ao <= 0f) { d = default; return; }
		float inv = 1f / ao;
		d = new Color(
			(byte)((s.R * sa + d.R * da * ia) * inv),
			(byte)((s.G * sa + d.G * da * ia) * inv),
			(byte)((s.B * sa + d.B * da * ia) * inv),
			(byte)(ao * 255f));
	}

	// Straight-alpha stamp of a `face`x`face` source into grid cell (cx, cy) of a
	// `dstW`-wide destination buffer. over=false copies, over=true alpha-composites.
	// Shared by every boss renderer that bakes a grid of casing faces (EBF body,
	// Soul Distiller segments, ...).
	public static void Stamp(Color[] dst, int dstW, Color[]? src, int face, int cx, int cy, bool over)
	{
		if (src is null) return;
		int ox = cx * face, oy = cy * face;
		for (int y = 0; y < face; y++)
			for (int x = 0; x < face; x++)
			{
				int di = (oy + y) * dstW + (ox + x);
				Color s = src[y * face + x];
				if (!over) dst[di] = s;
				else Over(ref dst[di], s);
			}
	}

	public static Color[] Upscale(Color[] src, int w, int h, int factor)
	{
		int uw = w * factor, uh = h * factor;
		var up = new Color[uw * uh];
		for (int y = 0; y < uh; y++)
			for (int x = 0; x < uw; x++)
				up[y * uw + x] = src[(y / factor) * w + (x / factor)];
		return up;
	}

	// Alpha-weighted box average (transparent pixels don't darken the mix).
	public static Color[] DownscaleBox(Color[] src, int sw, int sh, int dw, int dh)
	{
		var dst = new Color[dw * dh];
		for (int dy = 0; dy < dh; dy++)
			for (int dx = 0; dx < dw; dx++)
			{
				int sx0 = dx * sw / dw, sx1 = (dx + 1) * sw / dw;
				int sy0 = dy * sh / dh, sy1 = (dy + 1) * sh / dh;
				if (sx1 <= sx0) sx1 = sx0 + 1;
				if (sy1 <= sy0) sy1 = sy0 + 1;
				long aSum = 0, rSum = 0, gSum = 0, bSum = 0; int n = 0;
				for (int yy = sy0; yy < sy1; yy++)
					for (int xx = sx0; xx < sx1; xx++)
					{
						var s = src[yy * sw + xx];
						aSum += s.A; rSum += (long)s.R * s.A; gSum += (long)s.G * s.A; bSum += (long)s.B * s.A; n++;
					}
				byte a = (byte)(aSum / Math.Max(n, 1));
				dst[dy * dw + dx] = aSum == 0
					? default
					: new Color((byte)(rSum / aSum), (byte)(gSum / aSum), (byte)(bSum / aSum), a);
			}
		return dst;
	}

	public static Texture2D MakeTexture(Color[] px, int w, int h)
	{
		var tex = RuntimeTextureRegistry.New(w, h);
		tex.SetData(px);
		return tex;
	}

	// Bake a compact boss-head Asset by downscaling a body texture to headWxheadH.
	public static Asset<Texture2D>? BakeHeadAsset(Texture2D body, int headW, int headH, string name)
	{
		int sw = body.Width, sh = body.Height;
		if (sw <= 0 || sh <= 0) return null;
		var src = new Color[sw * sh];
		body.GetData(src);
		var tex = MakeTexture(DownscaleBox(src, sw, sh, headW, headH), headW, headH);
		return MachineRenderer.WrapAsset(tex, name);
	}

	// Draw a symmetric flapping vanilla-wing pair behind a boss body. The sheet
	// faces right; the left wing mirrors via FlipHorizontally. Each wing's origin
	// is pushed out by `bodyHalfWidthPx` (texture px) so it sits at the body's
	// side; screen offset scales with `scale`.
	public static void DrawWings(SpriteBatch sb, Vector2 center, Color color,
	                             int wingId, int wingFrames, int frame, float scale,
	                             float flap, float bodyHalfWidthPx)
	{
		if (Main.dedServ) return;
		Main.instance.LoadWings(wingId);
		var asset = TextureAssets.Wings[wingId];
		if (asset?.Value is not Texture2D wt || wt.Height < wingFrames) return;

		int fh = wt.Height / wingFrames;
		var src = new Rectangle(0, (frame % wingFrames) * fh, wt.Width, fh);
		Vector2 pos = center - new Vector2(0f, 8f * scale);

		sb.Draw(wt, pos, src, color, 0.12f + flap,
			new Vector2(-bodyHalfWidthPx, fh / 2f), scale, SpriteEffects.None, 0f);
		sb.Draw(wt, pos, src, color, -(0.12f + flap),
			new Vector2(wt.Width + bodyHalfWidthPx, fh / 2f), scale, SpriteEffects.FlipHorizontally, 0f);
	}
}
