#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Common.Materials;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;

// Texture renderer for material storage drums. Composites the drum's two-layer
// block face - verbatim of upstream's `cube_2_layer/tinted_bot` drum model:
//   base  = `storage/drums/drum/side`        (metal - tinted by material RGB)
//        or `storage/drums/wooden_drum/side` (wood  - untinted)
//   cap   = `storage/drums/drum_top/side`    (overlaid, never tinted)
// into a 36x36 Style2x2 tile sheet + a 32x32 item icon, installed into
// TextureAssets. Same install path as MaterialBlockRenderer - drums don't fit
// MachineRenderer's casing+overlay scheme, so they get this dedicated composite.
public static class DrumRenderer
{
	private const string TexRoot = "GregTechCEuTerraria/Content/Textures/";
	private const int Face = 16;

	private static readonly Dictionary<string, Color[]?> _layerCache = new(StringComparer.OrdinalIgnoreCase);
	private static readonly HashSet<int> _tilesDone = new();
	private static readonly HashSet<int> _itemsDone = new();

	// Build + install the 36x36 Style2x2 tile sheet for `tileType`. Idempotent.
	public static void EnsureTileTexture(int tileType, string? materialId)
	{
		if (!_tilesDone.Add(tileType)) return;
		var face = Compose(materialId);
		if (face is null) { _tilesDone.Remove(tileType); return; }
		var sheet = MachineRenderer.BuildStyle2x2Sheet(face);
		TextureAssets.Tile[tileType] = MachineRenderer.WrapAsset(sheet, $"gtceu_drum_tile_{tileType}");
	}

	// Build + install the 32x32 item icon for `itemType`. Idempotent.
	public static void EnsureItemTexture(int itemType, string? materialId)
	{
		if (!_itemsDone.Add(itemType)) return;
		var face = Compose(materialId);
		if (face is null) { _itemsDone.Remove(itemType); return; }
		var tex = RuntimeTextureRegistry.New(Face * 2, Face * 2);
		tex.SetData(Upscale2x(face));
		TextureAssets.Item[itemType] = MachineRenderer.WrapAsset(tex, $"gtceu_drum_item_{itemType}");
	}

	// Composite the drum's base + cap layers into one 16x16 face.
	private static Color[]? Compose(string? materialId)
	{
		bool wooden = materialId == "wood";
		var basePx = LoadLayer(wooden
			? "block/storage/drums/wooden_drum/side"
			: "block/storage/drums/drum/side");
		if (basePx is null) return null;
		var capPx = LoadLayer("block/storage/drums/drum_top/side");

		// Metal drum: the base layer is tinted by the material RGB (upstream
		// `tinted_bot`); a wooden drum keeps its native art (white = no tint).
		Color tint = Color.White;
		if (!wooden && materialId != null && MaterialRegistry.Get(materialId) is { Color: { } rgb })
			tint = new Color((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

		var acc = new Color[basePx.Length];
		for (int k = 0; k < acc.Length; k++)
			acc[k] = Multiply(basePx[k], tint);
		if (capPx != null)
			for (int k = 0; k < acc.Length && k < capPx.Length; k++)
				acc[k] = SrcOver(capPx[k], acc[k]);
		return acc;
	}

	// Loads a texture's top-left 16x16 pixels, cached by path.
	private static Color[]? LoadLayer(string texturePath)
	{
		if (_layerCache.TryGetValue(texturePath, out var cached)) return cached;
		string path = TexRoot + texturePath;
		if (!ModContent.HasAsset(path)) { _layerCache[texturePath] = null; return null; }

		var src = ModContent.Request<Texture2D>(path, AssetRequestMode.ImmediateLoad).Value;
		if (src.Width < Face || src.Height < Face) { _layerCache[texturePath] = null; return null; }
		var all = new Color[src.Width * src.Height];
		src.GetData(all);
		var px = new Color[Face * Face];
		for (int y = 0; y < Face; y++)
			for (int x = 0; x < Face; x++)
				px[y * Face + x] = all[y * src.Width + x];
		_layerCache[texturePath] = px;
		return px;
	}

	private static Color[] Upscale2x(Color[] face)
	{
		var up = new Color[Face * 2 * Face * 2];
		for (int y = 0; y < Face * 2; y++)
			for (int x = 0; x < Face * 2; x++)
				up[y * Face * 2 + x] = face[(y / 2) * Face + (x / 2)];
		return up;
	}

	private static Color Multiply(Color p, Color t) => new(
		p.R * t.R / 255, p.G * t.G / 255, p.B * t.B / 255, p.A * t.A / 255);

	private static Color SrcOver(Color src, Color dst)
	{
		float sa = src.A / 255f, da = dst.A / 255f;
		float oa = sa + da * (1f - sa);
		if (oa <= 0f) return default;
		byte Ch(int s, int d) => (byte)Math.Clamp((s * sa + d * da * (1f - sa)) / oa, 0f, 255f);
		return new Color(Ch(src.R, dst.R), Ch(src.G, dst.G), Ch(src.B, dst.B), (byte)(oa * 255f));
	}
}
