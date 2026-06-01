#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items;

// Single render path for every dump-driven item icon. Composites the upstream
// layer stack with per-layer tint, 2x nearest-neighbour upscales, installs the
// baked Texture2D into TextureAssets.Item[itemType]. Vanilla then draws it
// everywhere - inventory, world, AND held-item swing (the swing reads
// TextureAssets.Item with no PreDraw hook). Lazy + idempotent via HashSet.
//
// State-dependent visuals (battery charge, circuit config, fluid contents)
// STILL need PreDraw for inventory/world; this only installs the default-state
// image so the swing animation isn't broken.
//
// `Scale` (0..1; 1=default) lets a layer's source render smaller than its
// native size, centred on the canvas - used by WireItem to differentiate wire
// sizes from one shared `wire_end.png`. Scale>1 unsupported.
internal readonly record struct IconLayer(string TexturePath, Color Tint, float Scale = 1f)
{
	public IconLayer(string texturePath) : this(texturePath, Color.White, 1f) { }
	public IconLayer(string texturePath, Color tint) : this(texturePath, tint, 1f) { }
}

internal static class ItemIconBaker
{
	private const int UpscaleFactor = 2;
	private static readonly HashSet<int> _done = new();

	public static void Install(int itemType, params IconLayer[] layers)
	{
		if (itemType <= 0 || layers.Length == 0) return;
		if (Main.dedServ) return;                              // no GraphicsDevice
		if (!_done.Add(itemType)) return;

		Color[]? canvas = null;
		int w = 0, h = 0;
		foreach (var layer in layers)
		{
			if (!ModContent.HasAsset(layer.TexturePath)) continue;
			var tex = ModContent.Request<Texture2D>(layer.TexturePath, AssetRequestMode.ImmediateLoad).Value;
			if (canvas == null) { w = tex.Width; h = tex.Height; canvas = new Color[w * h]; }
			else if (tex.Width != w || tex.Height != h) continue; // size mismatch - skip layer

			var px = new Color[tex.Width * tex.Height];
			tex.GetData(px);
			Tint(px, layer.Tint);
			if (layer.Scale > 0f && layer.Scale < 1f)
				px = ScaleCentered(px, tex.Width, tex.Height, layer.Scale);
			AlphaCompositeOver(canvas, px);
		}
		if (canvas == null) { _done.Remove(itemType); return; }
		InstallPixels(itemType, canvas, w, h);
	}

	public static void Install(int itemType, string texturePath, Color tint) =>
		Install(itemType, new IconLayer(texturePath, tint));

	public static void Install(int itemType, string texturePath) =>
		Install(itemType, new IconLayer(texturePath, Color.White));

	private static void InstallPixels(int itemType, Color[] px, int w, int h)
	{
		var up = Upscale(px, w, h);
		int upW = w * UpscaleFactor, upH = h * UpscaleFactor;
		var baked = RuntimeTextureRegistry.New(upW, upH);
		baked.SetData(up);
		TextureAssets.Item[itemType] = MachineRenderer.WrapAsset(baked, $"gtceu_item_{itemType}");
	}

	// Per-channel multiply (alpha preserved). White = no-op.
	private static void Tint(Color[] px, Color tint)
	{
		if (tint == Color.White) return;
		for (int k = 0; k < px.Length; k++)
		{
			var p = px[k];
			px[k] = new Color(
				(byte)(p.R * tint.R / 255),
				(byte)(p.G * tint.G / 255),
				(byte)(p.B * tint.B / 255),
				p.A);
		}
	}

	// Straight-alpha Porter-Duff src-over (tML loads PNGs straight RGBA, so the
	// premultiplied form over-brightens edges). Same formula as MaterialBlockRenderer.SrcOver.
	private static void AlphaCompositeOver(Color[] dst, Color[] src)
	{
		for (int k = 0; k < dst.Length; k++)
		{
			var s = src[k];
			if (s.A == 0) continue;
			if (s.A == 255) { dst[k] = s; continue; }
			var d = dst[k];
			float sa = s.A / 255f, da = d.A / 255f;
			float ia = 1f - sa;
			float ao = sa + da * ia;
			if (ao <= 0f) { dst[k] = default; continue; }
			float inv = 1f / ao;
			dst[k] = new Color(
				(byte)((s.R * sa + d.R * da * ia) * inv),
				(byte)((s.G * sa + d.G * da * ia) * inv),
				(byte)((s.B * sa + d.B * da * ia) * inv),
				(byte)(ao * 255f));
		}
	}

	// Nearest-neighbour downscale + centred placement on a fresh wxh canvas.
	// One PNG renders at different visual sizes (WireItem per wire size)
	// without changing the bake's output dimensions.
	private static Color[] ScaleCentered(Color[] src, int w, int h, float scale)
	{
		int sw = System.Math.Max(1, (int)System.Math.Round(w * scale));
		int sh = System.Math.Max(1, (int)System.Math.Round(h * scale));
		int ox = (w - sw) / 2;
		int oy = (h - sh) / 2;
		var dst = new Color[w * h];
		for (int dy = 0; dy < sh; dy++)
		{
			int sy = dy * h / sh;
			for (int dx = 0; dx < sw; dx++)
			{
				int sx = dx * w / sw;
				dst[(oy + dy) * w + (ox + dx)] = src[sy * w + sx];
			}
		}
		return dst;
	}

	private static Color[] Upscale(Color[] src, int w, int h)
	{
		int uW = w * UpscaleFactor, uH = h * UpscaleFactor;
		var up = new Color[uW * uH];
		for (int y = 0; y < uH; y++)
			for (int x = 0; x < uW; x++)
				up[y * uW + x] = src[(y / UpscaleFactor) * w + (x / UpscaleFactor)];
		return up;
	}

	public static void ClearCache() => _done.Clear();
}
