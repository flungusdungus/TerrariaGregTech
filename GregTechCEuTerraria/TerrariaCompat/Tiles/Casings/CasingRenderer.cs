#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Casings;

// Bakes a casing block's flat texture (the upstream-mirror PNG named in the
// registry dump's `render.texture`) into a Style2x2 36x36 tile sheet + a 32x32
// item icon, installed into TextureAssets. One texture per casing, viewed flat
// - directional casings (voltage tiers) just use their `side` face.
//
// Active-aware casings (those with `render.activeTexture` in the dump) bake
// BOTH a default sheet AND an active sheet, kept in `_activeSheets`. The
// active sheet is sampled per-cell by CasingTile.PreDraw when the cell sits
// inside a running multiblock (ActiveCasingState.IsActive).
//
// Idempotent per type. Must run on the main thread (graphics) - invoked via
// the ITextureWarmUp first-frame pass on CasingTile / CasingItem.
internal static class CasingRenderer
{
	private const string TexRoot = "GregTechCEuTerraria/Content/Textures/";
	private static readonly HashSet<int> _tilesDone = new();
	private static readonly HashSet<int> _itemsDone = new();
	private static readonly Dictionary<int, Texture2D> _activeSheets = new();

	public static void EnsureTileTexture(int tileType, string blockTexture, string? activeTexture = null)
	{
		if (Main.dedServ || !_tilesDone.Add(tileType)) return;
		var face = LoadFace16(blockTexture);
		if (face is null) { _tilesDone.Remove(tileType); return; }
		var sheet = MachineRenderer.BuildStyle2x2Sheet(face);
		TextureAssets.Tile[tileType] = MachineRenderer.WrapAsset(sheet, $"gtceu_casing_tile_{tileType}");

		if (activeTexture is null) return;
		var activeFace = LoadFace16(activeTexture);
		if (activeFace is null) return;
		_activeSheets[tileType] = MachineRenderer.BuildStyle2x2Sheet(activeFace);
	}

	// Returns the pre-baked active sheet for this tile type, or null if the
	// casing has no active variant (or the active texture failed to load).
	public static Texture2D? GetActiveSheet(int tileType)
		=> _activeSheets.TryGetValue(tileType, out var t) ? t : null;

	public static void EnsureItemTexture(int itemType, string blockTexture)
	{
		if (Main.dedServ || !_itemsDone.Add(itemType)) return;
		var face = LoadFace16(blockTexture);
		if (face is null) { _itemsDone.Remove(itemType); return; }
		var icon = Upscale2x(face);
		var tex = RuntimeTextureRegistry.New(32, 32);
		tex.SetData(icon);
		TextureAssets.Item[itemType] = MachineRenderer.WrapAsset(tex, $"gtceu_casing_item_{itemType}");
	}

	// Load Content/Textures/<blockTexture>.png and resample its first square
	// frame down to a 16x16 Color[]. The square frame is `min(w,h)` on a side -
	// handles plain 16px textures, HD 32px (downscaled), and animated vertical
	// strips (the top wxw block is frame 0).
	internal static Color[]? LoadFace16(string blockTexture)
	{
		string path = TexRoot + blockTexture;
		if (!ModContent.HasAsset(path)) return null;

		Texture2D src;
		try { src = ModContent.Request<Texture2D>(path, AssetRequestMode.ImmediateLoad).Value; }
		catch { return null; }

		int w = src.Width, h = src.Height;
		if (w <= 0 || h <= 0) return null;

		var srcPx = new Color[w * h];
		src.GetData(srcPx);

		int frame = Math.Min(w, h);
		var face = new Color[16 * 16];
		for (int y = 0; y < 16; y++)
			for (int x = 0; x < 16; x++)
				face[y * 16 + x] = srcPx[(y * frame / 16) * w + (x * frame / 16)];
		return face;
	}

	private static Color[] Upscale2x(Color[] px16)
	{
		var up = new Color[32 * 32];
		for (int y = 0; y < 32; y++)
			for (int x = 0; x < 32; x++)
				up[y * 32 + x] = px16[(y / 2) * 16 + (x / 2)];
		return up;
	}
}
