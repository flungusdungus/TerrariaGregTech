#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.BossDrops.MultiblockBag;

// Bakes a multiblock-bag icon at runtime: vanilla Moon Lord treasure bag base
// + the multi's controller icon scissored to a square and composited onto the
// bag's center. One texture per bag, installed lazily on first WarmUpTexture
// call (idempotent - guarded by `_done`).
//
// Both source textures are byte arrays straight off the GPU (GetData). The
// overlay is read AFTER MachineRenderer.EnsureItemTexture is called on the
// controller item, so the controller's composite is guaranteed installed even
// if its own WarmUp pass hasn't run yet.
internal static class MultiblockBagArt
{
	private static readonly HashSet<int> _done = new();

	public static void InstallFor(int bagItemType, string multiId)
	{
		if (Main.dedServ) return;
		if (!_done.Add(bagItemType)) return;

		var basePixels = LoadBagBase(out int bagW, out int bagH);
		if (basePixels is null) return;

		var overlay = LoadControllerOverlay(multiId, out int ovW, out int ovH);
		if (overlay is null)
		{
			// No overlay - still install the plain bag so the slot doesn't show
			// the TooManyItems placeholder. Future runs find the controller
			// composite ready and re-install via the `_done` guard... except the
			// guard already flipped, so leave the plain bag for this session.
			InstallTexture(bagItemType, basePixels, bagW, bagH);
			return;
		}

		// Scale overlay down to roughly 60% of the bag's width so it sits inside
		// the bag silhouette without clipping the strap/knot at the top.
		int target = System.Math.Max(8, (int)(bagW * 0.6f));
		var scaled = NearestScale(overlay, ovW, ovH, target, target);

		int ox = (bagW - target) / 2;
		int oy = (bagH - target) / 2 + 2; // bias down 2 px so the strap stays visible
		CompositeOver(basePixels, bagW, bagH, scaled, target, target, ox, oy);

		InstallTexture(bagItemType, basePixels, bagW, bagH);
	}

	// === Source loaders =====================================================

	private static Color[]? LoadBagBase(out int w, out int h)
	{
		w = h = 0;
		// Vanilla item textures live in TextureAssets.Item, indexed by ItemID.
		// They're lazy - Main.instance.LoadItem ensures the asset is loaded
		// (mirrors vanilla's own pre-draw guard in Main.DrawItem).
		Main.instance.LoadItem(ItemID.MoonLordBossBag);
		var asset = TextureAssets.Item[ItemID.MoonLordBossBag];
		if (asset?.Value is not Texture2D tex) return null;
		w = tex.Width;
		h = tex.Height;
		var px = new Color[w * h];
		tex.GetData(px);
		return px;
	}

	private static Color[]? LoadControllerOverlay(string multiId, out int w, out int h)
	{
		w = h = 0;
		if (!MachineRegistry.TryGet(multiId, out var def)) return null;

		// Resolve the controller item by its tile-shared Name (= MachineKey for
		// tiered defs, bare id for non-tiered). Same convention used everywhere.
		var tier = def.Tiers.Length > 0 ? def.Tiers[0] : Common.Energy.VoltageTier.LV;
		string ctrlName = def.Tiered ? $"{Common.Energy.VoltageTiers.Id(tier)}_{def.Id}" : def.Id;
		var mod = ModLoader.GetMod("GregTechCEuTerraria");
		if (!mod.TryFind<ModItem>(ctrlName, out var ctrl)) return null;

		// Force the controller's runtime composite to install before we read it
		// - otherwise we'd grab the autoload placeholder PNG (the TMI dark plate)
		// instead of the real machine icon.
		if (ctrl is TieredMachineItem tmi) tmi.WarmUpTexture();

		var asset = TextureAssets.Item[ctrl.Type];
		if (asset?.Value is not Texture2D tex) return null;
		w = tex.Width;
		h = tex.Height;
		var px = new Color[w * h];
		tex.GetData(px);
		return px;
	}

	// === Pixel ops ==========================================================

	// Nearest-neighbour resize - pixel-art friendly. Source / dest must be RGBA.
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

	// Straight-alpha src-over of `src` onto `dst` at (ox, oy). Same convention
	// TooManyItemsArt uses - tML loads PNGs as straight RGBA, not premultiplied.
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
					(byte)System.Math.Min(255, s.A + d.A * ia));
			}
		}
	}

	private static void InstallTexture(int itemType, Color[] pixels, int w, int h)
	{
		var tex = RuntimeTextureRegistry.New(w, h);
		tex.SetData(pixels);
		TextureAssets.Item[itemType] = MachineRenderer.WrapAsset(tex, $"multiblock_bag_{itemType}");
	}
}
