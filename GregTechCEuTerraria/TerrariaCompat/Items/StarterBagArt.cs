#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using GregTechCEuTerraria.TerrariaCompat.Recipes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items;

// Bakes starter-bag icon: Moon Lord treasure-bag base + the overlay item's
// icon centred on it. Same shape as MultiblockBagArt - reads the overlay
// AFTER the source's WarmUpTexture so layered composites are ready.
internal static class StarterBagArt
{
	private static readonly HashSet<int> _done = new();

	public static void InstallFor(int bagItemType, string overlayUpstreamId) =>
		InstallFor(bagItemType, IngredientResolverImpl.Instance.ResolveItemType(overlayUpstreamId));

	public static void InstallFor(int bagItemType, int overlayItemType)
	{
		if (Main.dedServ) return;
		if (!_done.Add(bagItemType)) return;

		var basePixels = LoadBagBase(out int bagW, out int bagH);
		if (basePixels is null) return;

		var overlay = LoadOverlay(overlayItemType, out int ovW, out int ovH);
		if (overlay is null) { InstallTexture(bagItemType, basePixels, bagW, bagH); return; }

		int target = Math.Max(8, (int)(bagW * 0.6f));
		var scaled = NearestScale(overlay, ovW, ovH, target, target);
		int ox = (bagW - target) / 2;
		int oy = (bagH - target) / 2 + 2;          // bias down for the strap
		CompositeOver(basePixels, bagW, bagH, scaled, target, target, ox, oy);
		InstallTexture(bagItemType, basePixels, bagW, bagH);
	}

	private static Color[]? LoadBagBase(out int w, out int h)
	{
		w = h = 0;
		Main.instance.LoadItem(ItemID.MoonLordBossBag);
		var asset = TextureAssets.Item[ItemID.MoonLordBossBag];
		if (asset?.Value is not Texture2D tex) return null;
		w = tex.Width; h = tex.Height;
		var px = new Color[w * h];
		tex.GetData(px);
		return px;
	}

	private static Color[]? LoadOverlay(int type, out int w, out int h)
	{
		w = h = 0;
		if (type <= 0) return null;

		// Force the source's runtime composite (else we'd grab the autoload
		// placeholder). ItemLoader.GetItem returns the template - same pattern
		// the Gregith icon-cycling uses.
		if (ItemLoader.GetItem(type) is ITextureWarmUp warm) warm.WarmUpTexture();

		Main.instance.LoadItem(type);
		var asset = TextureAssets.Item[type];
		if (asset?.Value is not Texture2D tex) return null;
		w = tex.Width; h = tex.Height;
		var px = new Color[w * h];
		tex.GetData(px);
		return px;
	}

	private static Color[] NearestScale(Color[] src, int sw, int sh, int dw, int dh)
	{
		var dst = new Color[dw * dh];
		for (int y = 0; y < dh; y++)
		{
			int sy = y * sh / dh;
			for (int x = 0; x < dw; x++)
			{
				int sx = x * sw / dw;
				dst[y * dw + x] = src[sy * sw + sx];
			}
		}
		return dst;
	}

	private static void CompositeOver(Color[] dst, int dw, int dh,
	                                  Color[] src, int sw, int sh,
	                                  int ox, int oy)
	{
		for (int y = 0; y < sh; y++)
		{
			int dy = oy + y;
			if (dy < 0 || dy >= dh) continue;
			for (int x = 0; x < sw; x++)
			{
				int dx = ox + x;
				if (dx < 0 || dx >= dw) continue;
				var s = src[y * sw + x];
				if (s.A == 0) continue;
				if (s.A == 255) { dst[dy * dw + dx] = s; continue; }
				var d = dst[dy * dw + dx];
				float sa = s.A / 255f, ia = 1f - sa;
				dst[dy * dw + dx] = new Color(
					(byte)(s.R * sa + d.R * ia),
					(byte)(s.G * sa + d.G * ia),
					(byte)(s.B * sa + d.B * ia),
					(byte)Math.Min(255, s.A + d.A * ia));
			}
		}
	}

	private static void InstallTexture(int itemType, Color[] pixels, int w, int h)
	{
		var tex = RuntimeTextureRegistry.New(w, h);
		tex.SetData(pixels);
		TextureAssets.Item[itemType] = MachineRenderer.WrapAsset(tex, $"starter_bag_{itemType}");
	}
}
