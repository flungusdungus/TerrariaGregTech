#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Items.Registry;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;

// Per-material storage block tile renderer. Composites the dump's render
// layers into a 36x36 Style2x2 sheet installed into TextureAssets.Tile -
// same layer set MaterialItem draws + upstream parity. Placement ghost +
// minimap read TextureAssets.Tile directly; vanilla adds its Lighting tint.
public static class MaterialBlockRenderer
{
	private const string TexRoot = "GregTechCEuTerraria/Content/Textures/";

	private static readonly Dictionary<string, Color[]?> _layerCache = new(StringComparer.OrdinalIgnoreCase);
	private static readonly HashSet<int> _tilesDone = new();

	public static void EnsureTileTexture(int tileType, string blockItemId)
	{
		if (!_tilesDone.Add(tileType)) return;
		var face = Compose(blockItemId);
		if (face is null) return;
		var sheet = MachineRenderer.BuildStyle2x2Sheet(face);
		TextureAssets.Tile[tileType] = MachineRenderer.WrapAsset(sheet, $"gtceu_mat_block_{tileType}");
	}

	private static Color[]? Compose(string blockItemId)
	{
		if (!RegistryDump.TryGet(blockItemId, out var e) ||
		    e.RenderLayers is not { Count: > 0 } layers)
			return null;

		Color[]? acc = null;
		foreach (var layer in layers)
		{
			var px = LoadLayer(layer.Texture);
			if (px is null) continue;
			var tint = ArgbColor(layer.Argb);
			acc ??= new Color[px.Length];
			for (int k = 0; k < px.Length && k < acc.Length; k++)
				acc[k] = SrcOver(Multiply(px[k], tint), acc[k]);
		}
		return acc;
	}

	private static Color[]? LoadLayer(string texturePath)
	{
		if (_layerCache.TryGetValue(texturePath, out var cached)) return cached;

		string path = TexRoot + texturePath;
		if (!ModContent.HasAsset(path)) { _layerCache[texturePath] = null; return null; }

		var src = ModContent.Request<Texture2D>(path, AssetRequestMode.ImmediateLoad).Value;
		if (src.Width < 16 || src.Height < 16) { _layerCache[texturePath] = null; return null; }

		Color[] all = new Color[src.Width * src.Height];
		src.GetData(all);
		Color[] pixels = new Color[16 * 16];
		for (int y = 0; y < 16; y++)
			for (int x = 0; x < 16; x++)
				pixels[y * 16 + x] = all[y * src.Width + x];

		_layerCache[texturePath] = pixels;
		return pixels;
	}

	// Upstream getLayerARGB 0xAARRGGBB; -1 = untinted white.
	private static Color ArgbColor(int argb) => new(
		(byte)((argb >> 16) & 0xFF),
		(byte)((argb >> 8) & 0xFF),
		(byte)(argb & 0xFF),
		(byte)((argb >> 24) & 0xFF));

	private static Color Multiply(Color p, Color t) => new(
		p.R * t.R / 255, p.G * t.G / 255, p.B * t.B / 255, p.A * t.A / 255);

	// Straight-alpha src-over.
	private static Color SrcOver(Color src, Color dst)
	{
		float sa = src.A / 255f, da = dst.A / 255f;
		float oa = sa + da * (1f - sa);
		if (oa <= 0f) return default;
		byte Ch(int s, int d) =>
			(byte)Math.Clamp((s * sa + d * da * (1f - sa)) / oa, 0f, 255f);
		return new Color(Ch(src.R, dst.R), Ch(src.G, dst.G), Ch(src.B, dst.B), (byte)(oa * 255f));
	}
}
