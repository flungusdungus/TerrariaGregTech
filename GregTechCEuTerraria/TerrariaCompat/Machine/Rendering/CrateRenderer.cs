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

// Texture renderer for material storage crates - verbatim of upstream's crate
// model: a single all-faces texture, `storage/crates/metal_crate` tinted by the
// material RGB (`cube/tinted/all`) or `storage/crates/wooden_crate` untinted
// (`cube_all`). Bakes into the 36x36 Style2x2 tile sheet + a 32x32 item icon.
// Same install path as DrumRenderer / MaterialBlockRenderer.
public static class CrateRenderer
{
	private const string TexRoot = "GregTechCEuTerraria/Content/Textures/";
	private const int Face = 16;

	private static readonly Dictionary<string, Color[]?> _layerCache = new(StringComparer.OrdinalIgnoreCase);
	private static readonly HashSet<int> _tilesDone = new();
	private static readonly HashSet<int> _itemsDone = new();

	public static void EnsureTileTexture(int tileType, string? materialId)
	{
		if (!_tilesDone.Add(tileType)) return;
		var face = Compose(materialId);
		if (face is null) { _tilesDone.Remove(tileType); return; }
		var sheet = MachineRenderer.BuildStyle2x2Sheet(face);
		TextureAssets.Tile[tileType] = MachineRenderer.WrapAsset(sheet, $"gtceu_crate_tile_{tileType}");
	}

	public static void EnsureItemTexture(int itemType, string? materialId)
	{
		if (!_itemsDone.Add(itemType)) return;
		var face = Compose(materialId);
		if (face is null) { _itemsDone.Remove(itemType); return; }
		var tex = RuntimeTextureRegistry.New(Face * 2, Face * 2);
		tex.SetData(Upscale2x(face));
		TextureAssets.Item[itemType] = MachineRenderer.WrapAsset(tex, $"gtceu_crate_item_{itemType}");
	}

	private static Color[]? Compose(string? materialId)
	{
		bool wooden = materialId == "wood";
		var basePx = LoadLayer(wooden
			? "block/storage/crates/wooden_crate"
			: "block/storage/crates/metal_crate");
		if (basePx is null) return null;

		// Metal crate is tinted by the material RGB (upstream `cube/tinted/all`);
		// the wooden crate keeps its native art (`cube_all` - no tint).
		Color tint = Color.White;
		if (!wooden && materialId != null && MaterialRegistry.Get(materialId) is { Color: { } rgb })
			tint = new Color((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

		var acc = new Color[basePx.Length];
		for (int k = 0; k < acc.Length; k++)
			acc[k] = Multiply(basePx[k], tint);
		return acc;
	}

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
}
