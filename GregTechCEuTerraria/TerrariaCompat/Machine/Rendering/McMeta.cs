#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;

// Parser for vanilla `.png.mcmeta` animation blocks. Object-form per-frame
// entries fall back to sequential (frames = null). Missing file -> (1, null).
public static class McMeta
{
	public static (int FrameTime, int[]? Frames) Read(Mod mod, string modRelPath)
	{
		try
		{
			// GetFileStream THROWS for an absent file - gate on FileExists.
			if (mod is null || !mod.FileExists(modRelPath)) return (1, null);
			using var stream = mod.GetFileStream(modRelPath);
			if (stream is null) return (1, null);
			using var doc = JsonDocument.Parse(stream);
			if (!doc.RootElement.TryGetProperty("animation", out var anim)) return (1, null);

			int frameTime = anim.TryGetProperty("frametime", out var ft) && ft.ValueKind == JsonValueKind.Number
				? Math.Max(1, ft.GetInt32())
				: 1;

			int[]? frames = null;
			if (anim.TryGetProperty("frames", out var fr) && fr.ValueKind == JsonValueKind.Array)
			{
				var list = new List<int>();
				foreach (var e in fr.EnumerateArray())
				{
					if (e.ValueKind != JsonValueKind.Number) { list = null; break; }
					list.Add(e.GetInt32());
				}
				if (list is { Count: > 0 }) frames = list.ToArray();
			}
			return (frameTime, frames);
		}
		catch
		{
			return (1, null);
		}
	}
}
