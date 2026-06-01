#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.TerrariaCompat.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;

// Fluid-still resolver+drawer for UI (recipe browser / tank widgets).
// Mirrors FluidBuilder.determineTextures: custom art at block/fluids/fluid.<id>
// (untinted) vs generic per-icon-type material_sets/dull/{liquid|gas|plasma|molten}
// tinted by fluid colour. .mcmeta sequence (pingpong) is honoured.
public static class FluidIconRenderer
{
	private const string TexRoot = "GregTechCEuTerraria/Content/Textures";

	public readonly record struct FluidIcon(Texture2D? Tex, bool Tint, int[] Sequence, int FrameTime);

	private static readonly Dictionary<string, FluidIcon> _byFluid = new();
	private static readonly Dictionary<string, FluidIcon> _byPath  = new();

	public static FluidIcon Resolve(FluidType fluid)
	{
		if (_byFluid.TryGetValue(fluid.Id, out var hit)) return hit;

		FluidIcon icon;
		// File presence is the signal (upstream's customStill always disables
		// colour). NOT gated on IsColorEnabled - the dump doesn't carry it.
		if (Load($"block/fluids/fluid.{fluid.Id}", tint: false) is { Tex: not null } custom)
			icon = custom;
		else
			icon = ResolveGeneric(fluid);

		_byFluid[fluid.Id] = icon;
		return icon;
	}

	private static FluidIcon ResolveGeneric(FluidType fluid)
	{
		// Icon types only ship under the `dull` set.
		string iconType = fluid.SourceKey?.IconType ?? "liquid";
		var icon = Load($"block/material_sets/dull/{iconType}", tint: true);
		if (icon is { Tex: not null }) return icon.Value;
		return Load($"block/material_sets/dull/liquid", tint: true)
		       ?? new FluidIcon(null, true, Array.Empty<int>(), 1);
	}

	// Cached per-path (many fluids share the generic texture).
	private static FluidIcon? Load(string relPath, bool tint)
	{
		if (_byPath.TryGetValue(relPath, out var cached))
			return cached.Tex is null ? null : cached;

		string assetPath = $"{TexRoot}/{relPath}";
		if (!ModContent.HasAsset(assetPath))
		{
			_byPath[relPath] = default;
			return null;
		}

		var tex  = ModContent.Request<Texture2D>(assetPath, AssetRequestMode.ImmediateLoad).Value;
		int side = tex.Width <= 0 ? 1 : tex.Width;
		int physicalFrames = Math.Max(1, tex.Height / side);

		var (frameTime, frames) = ReadAnimation($"Content/Textures/{relPath}.png.mcmeta");
		int[] sequence = frames ?? Sequential(physicalFrames);
		// Defensive clamp.
		for (int i = 0; i < sequence.Length; i++)
			if (sequence[i] < 0 || sequence[i] >= physicalFrames) sequence[i] = 0;

		var result = new FluidIcon(tex, tint, sequence, frameTime);
		_byPath[relPath] = result;
		return result;
	}

	private static int[] Sequential(int n)
	{
		var a = new int[n];
		for (int i = 0; i < n; i++) a[i] = i;
		return a;
	}

	private static (int FrameTime, int[]? Frames) ReadAnimation(string modRelPath) =>
		McMeta.Read(ModContent.GetInstance<GregTechCEuTerraria>(), modRelPath);

	// Draws a fluid still into `dest` (point-clamped). UI callers leave `light`
	// null; world-tile callers pass Lighting.GetColor. Returns false when no
	// texture resolved (caller draws a flat fill).
	public static bool Draw(SpriteBatch sb, FluidType fluid, Rectangle dest, float alpha = 1f, Color? light = null)
	{
		var icon = Resolve(fluid);
		if (icon.Tex is null || icon.Sequence.Length == 0) return false;

		int side    = icon.Tex.Width;
		int seqIdx  = (int)(Main.GameUpdateCount / (uint)icon.FrameTime % (uint)icon.Sequence.Length);
		int physical = icon.Sequence[seqIdx];
		var src = new Rectangle(0, physical * side, side, side);

		Color baseCol = icon.Tint ? RgbColor(fluid.Color) : Color.White;
		if (light is { } l)
			baseCol = new Color(baseCol.R * l.R / 255, baseCol.G * l.G / 255, baseCol.B * l.B / 255);
		Color drawTint = baseCol * alpha;
		PointClampDraw.Draw(sb, () => sb.Draw(icon.Tex, dest, src, drawTint));
		return true;
	}

	public static Color RgbColor(uint c) =>
		new((byte)((c >> 16) & 0xFF), (byte)((c >> 8) & 0xFF), (byte)(c & 0xFF));
}
