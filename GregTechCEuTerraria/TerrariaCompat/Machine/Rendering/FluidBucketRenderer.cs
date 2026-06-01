#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Fluids;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;

// Per-fluid bucket icon. Empty Bucket base + a "water mask" derived from the
// Water Bucket sprite (any pixel that differs from Empty). Fluid pixels are
// tinted by water luminance / avg so vanilla shading carries over.
public static class FluidBucketRenderer
{
	private static readonly HashSet<int> _done = new();

	private static Color[]? _empty;
	private static Color[]? _water;
	private static int _w, _h;
	private static float _avgWaterLum = 128f;

	public static void EnsureItemTexture(int itemType, FluidType fluid)
	{
		if (!_done.Add(itemType)) return;
		if (!EnsureBaseSprites()) return;

		Color fc = RgbColor(fluid.Color);
		var px = new Color[_w * _h];
		for (int i = 0; i < px.Length; i++)
		{
			Color e = _empty![i], w = _water![i];
			px[i] = IsWaterPixel(e, w) ? TintWater(w, fc) : e;
		}

		var tex = RuntimeTextureRegistry.New(_w, _h);
		tex.SetData(px);
		TextureAssets.Item[itemType] = MachineRenderer.WrapAsset(tex, $"gtceu_bucket_{itemType}");
	}

	// False on missing/mismatched sprites -> caller leaves placeholder.
	private static bool EnsureBaseSprites()
	{
		if (_empty != null) return _w > 0;

		var empty = LoadVanillaItem(ItemID.EmptyBucket);
		var water = LoadVanillaItem(ItemID.WaterBucket);
		if (empty is null || water is null
		    || empty.Width != water.Width || empty.Height != water.Height)
		{
			_empty = Array.Empty<Color>();   // mark attempted
			return false;
		}

		_w = empty.Width;
		_h = empty.Height;
		_empty = new Color[_w * _h];
		_water = new Color[_w * _h];
		empty.GetData(_empty);
		water.GetData(_water);

		long sum = 0;
		int count = 0;
		for (int i = 0; i < _empty.Length; i++)
			if (IsWaterPixel(_empty[i], _water[i])) { sum += Lum(_water[i]); count++; }
		_avgWaterLum = count > 0 ? sum / (float)count : 128f;
		return true;
	}

	private static Texture2D? LoadVanillaItem(int itemId)
	{
		string path = $"Terraria/Images/Item_{itemId}";
		return ModContent.HasAsset(path)
			? ModContent.Request<Texture2D>(path, AssetRequestMode.ImmediateLoad).Value
			: null;
	}

	private static bool IsWaterPixel(Color empty, Color water) =>
		water.A > 0 && water.PackedValue != empty.PackedValue;

	private static int Lum(Color c) => (c.R + c.G + c.B) / 3;

	// Tint by water luminance / avg - avg pixel maps to exactly fc.
	private static Color TintWater(Color water, Color fc)
	{
		float t = Lum(water) / _avgWaterLum;
		return new Color(
			(byte)Math.Min(255, fc.R * t),
			(byte)Math.Min(255, fc.G * t),
			(byte)Math.Min(255, fc.B * t),
			water.A);
	}

	private static Color RgbColor(uint c) =>
		new((byte)((c >> 16) & 0xFF), (byte)((c >> 8) & 0xFF), (byte)(c & 0xFF));
}
