#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace GregTechCEuTerraria;

// Central registry for every runtime-baked Texture2D the mod composites.
//
// WHY: FNA's GraphicsDevice keeps a strong-reference list of live
// GraphicsResources (for device-reset recreation), and that list lives in
// FNA - outside our collectible mod ALC. Any undisposed mod-created texture
// pins the entire ALC on reload (big slice of the ~1 GB/reload leak).
// Route every mod texture through New() and Dispose all in Mod.Unload().
//
// Lives in the root namespace so renderers resolve it unqualified.
// ONLY mod-created textures - never tML/ReLogic-owned ones (TextureAssets,
// ModContent.Request results).
public static class RuntimeTextureRegistry
{
	private static readonly List<Texture2D> _tracked = new();
	private static readonly object _lock = new();

	public static Texture2D New(int width, int height)
	{
		var tex = new Texture2D(Main.graphics.GraphicsDevice, width, height);
		lock (_lock) _tracked.Add(tex);
		return tex;
	}

	// Track a texture we own but didn't allocate via New() - e.g. the texture
	// ReLogic decodes inside Main.Assets.CreateUntracked (the copy stored in
	// TextureAssets; "untracked" means ReLogic won't dispose it, so we must).
	public static void Track(Texture2D? tex)
	{
		if (tex is null) return;
		lock (_lock) _tracked.Add(tex);
	}

	// Called from Mod.Unload. Safe on dedicated server (list is empty).
	public static void DisposeAll()
	{
		lock (_lock)
		{
			foreach (var tex in _tracked)
			{
				try { if (tex is { IsDisposed: false }) tex.Dispose(); }
				catch { }
			}
			_tracked.Clear();
		}
	}
}
