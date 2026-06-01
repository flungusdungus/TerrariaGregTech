#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Common.Materials;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;

// Draw-time per-material ore tile renderer. Vein-shape PNG sampled untinted
// from the iconset chain; tint = lighting x material_color computed inline -
// single shader multiply, equivalent to a pre-bake.
public static class OreRenderer
{
	private const string TexRoot = "GregTechCEuTerraria/Content/Textures";

	// Wraps tML's Asset cache to skip repeated HasAsset probes.
	private static readonly Dictionary<string, Asset<Texture2D>?> _veinByIconset =
		new(System.StringComparer.OrdinalIgnoreCase);

	public static Asset<Texture2D>? GetVeinAsset(string? iconSet)
	{
		string key = (iconSet ?? "METALLIC").ToLowerInvariant();
		if (_veinByIconset.TryGetValue(key, out var cached)) return cached;

		Asset<Texture2D>? result = null;
		foreach (var cand in IconSetHierarchy.WalkChain(iconSet))
		{
			string path = $"{TexRoot}/block/material_sets/{cand.ToLowerInvariant()}/ore";
			if (!ModContent.HasAsset(path)) continue;
			result = ModContent.Request<Texture2D>(path, AssetRequestMode.ImmediateLoad);
			break;
		}
		_veinByIconset[key] = result;
		return result;
	}

	// XNA has no Color x Color; this is the shader's per-channel multiply, A=255.
	public static Color MultiplyRGB(Color a, Color b) =>
		new(a.R * b.R / 255, a.G * b.G / 255, a.B * b.B / 255, (byte)255);
}
