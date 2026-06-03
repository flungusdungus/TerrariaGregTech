#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GregTechCEuTerraria.Common.Energy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;

// Composites upstream layers into a Texture2D and installs into
// TextureAssets.Tile/Item so vanilla draws machines (inventory, world,
// placement ghost, minimap) through its normal pipeline.
//
// PreDraw wouldn't work for the placement ghost / minimap (they read
// TextureAssets directly, no hook). Lazy build per tier so unused tiers
// pay nothing.
public static class MachineRenderer
{
	private const int Face = 16;          // upstream face PNG
	private const int Up   = 32;          // 2x upscale
	private const int Cell = 18;          // Style2x2 stride (16 + 2 gutter)
	private const uint ActiveTicksPerFrame = 4;

	public enum Casing
	{
		None,              // overlay is the whole face (SolarPanel)
		Voltage,           // voltage-tier hull
		BrickedBronze,     // LP steam
		BrickedSteel,      // HP steam
		CokeBricks,        // coke_oven multi + hatch
		Firebricks,        // primitive_blast_furnace
		PumpDeck,          // primitive_pump + pump_hatch
	}

	private static readonly HashSet<int> _tilesDone = new();
	private static readonly HashSet<int> _itemsDone = new();

	public static void EnsureTileTexture(int tileType, IMachineTextureSpec spec, VoltageTier tier)
	{
		if (!_tilesDone.Add(tileType)) return;
		var face = CompositeFace(spec, tier, active: false, frame: 0);
		if (face is null) return;
		var sheet = BuildStyle2x2Sheet(face);
		TextureAssets.Tile[tileType] = WrapAsset(sheet, $"gtceu_tile_{tileType}");
	}

	public static void EnsureItemTexture(int itemType, IMachineTextureSpec spec, VoltageTier tier)
	{
		if (!_itemsDone.Add(itemType)) return;
		var face = CompositeFace(spec, tier, active: false, frame: 0);
		if (face is null) return;
		var icon = Upscale2x(face);
		var tex  = RuntimeTextureRegistry.New(Up, Up);
		tex.SetData(icon);
		TextureAssets.Item[itemType] = WrapAsset(tex, $"gtceu_item_{itemType}");
	}

	// Conditional overlay (idle PNG, no animation strip). Used by the rotor
	// holder's IsFormed-gated overlay_rotor_holder frame.
	public static void DrawStaticOverlay(SpriteBatch sb, int i, int j,
	                                     string overlayDir, string overlayBasename)
	{
		var tex = GetStaticOverlay(overlayDir, overlayBasename);
		if (tex is null) return;

		Tile tile = Main.tile[i, j];
		int cornerX = tile.TileFrameX >= Cell ? 1 : 0;
		int cornerY = tile.TileFrameY >= Cell ? 1 : 0;

		Vector2 zero = Main.drawToScreen ? Vector2.Zero : new Vector2(Main.offScreenRange, Main.offScreenRange);
		Vector2 pos  = new Vector2(i * 16 - (int)Main.screenPosition.X,
		                           j * 16 - (int)Main.screenPosition.Y) + zero;
		Color light  = Lighting.GetColor(i, j);
		var src = new Rectangle(cornerX * (Up / 2), cornerY * (Up / 2), Up / 2, Up / 2);
		sb.Draw(tex, pos, src, light, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
	}

	private static readonly Dictionary<string, Texture2D?> _staticOverlays = new();
	private static Texture2D? GetStaticOverlay(string overlayDir, string overlayBasename)
	{
		string key = $"{overlayDir}/{overlayBasename}";
		if (_staticOverlays.TryGetValue(key, out var cached)) return cached;
		var raw = LoadOverlay(overlayDir, overlayBasename, active: false);
		var up  = raw is null ? null : UpscaleTexture2x(raw);
		_staticOverlays[key] = up;
		return up;
	}

	// Animated overlay drawn over the vanilla base. Pre-upscaled 2x then
	// drawn 1:1 for sampler-agnostic pixel precision.
	public static void DrawActiveOverlay(SpriteBatch sb, int i, int j,
	                                     string overlayDir, string overlayBasename)
	{
		var tex = GetActiveOverlay(overlayDir, overlayBasename);
		if (tex is null) return;

		Tile tile = Main.tile[i, j];
		int cornerX = tile.TileFrameX >= Cell ? 1 : 0;
		int cornerY = tile.TileFrameY >= Cell ? 1 : 0;

		// Frames are Up-px-tall bands stacked vertically.
		int frameY = 0;
		if (tex.Height > Up)
		{
			int frames = tex.Height / Up;
			int frame  = (int)((Main.GameUpdateCount / ActiveTicksPerFrame) % (uint)frames);
			frameY = frame * Up;
		}

		Vector2 zero = Main.drawToScreen ? Vector2.Zero : new Vector2(Main.offScreenRange, Main.offScreenRange);
		Vector2 pos  = new Vector2(i * 16 - (int)Main.screenPosition.X,
		                           j * 16 - (int)Main.screenPosition.Y) + zero;
		Color light  = Lighting.GetColor(i, j);
		var src = new Rectangle(cornerX * (Up / 2), frameY + cornerY * (Up / 2), Up / 2, Up / 2);
		sb.Draw(tex, pos, src, light, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
	}

	// Reskin a multiblock part with its controller's appearance casing
	// (upstream IS_FORMED model property + multi's casing reference).
	private static readonly Dictionary<(int partTileType, int fusedCasingTileType, VoltageTier tier), Texture2D?>
		_fusedSheets = new();

	public static Texture2D? GetFusedSheet(int partTileType, IMachineTextureSpec spec, VoltageTier tier,
	                                       ushort fusedCasingTileType, string? fusedCasingTexturePath)
	{
		var key = (partTileType, (int)fusedCasingTileType, tier);
		if (_fusedSheets.TryGetValue(key, out var cached)) return cached;

		// Prefer the source PNG so we don't dig through Style2x2 gutters.
		Color[]? fusedCasingFace = null;
		if (!string.IsNullOrEmpty(fusedCasingTexturePath))
			fusedCasingFace = Tiles.Casings.CasingRenderer.LoadFace16(fusedCasingTexturePath);
		if (fusedCasingFace is null) { _fusedSheets[key] = null; return null; }

		var face = CompositeFace(spec, tier, active: false, frame: 0,
			overrideCasingFace: fusedCasingFace);
		if (face is null) { _fusedSheets[key] = null; return null; }
		var sheet = BuildStyle2x2Sheet(face);
		_fusedSheets[key] = sheet;
		return sheet;
	}

	// Draw the fused composite at cell (i, j). Mirrors `DrawActiveOverlay`'s
	// per-cell math - picks the right Style2x2 quadrant from the sheet.
	public static void DrawFusedComposite(SpriteBatch sb, int i, int j, Texture2D sheet)
	{
		Tile tile = Main.tile[i, j];
		int cornerX = tile.TileFrameX >= Cell ? 1 : 0;
		int cornerY = tile.TileFrameY >= Cell ? 1 : 0;

		Vector2 zero = Main.drawToScreen ? Vector2.Zero : new Vector2(Main.offScreenRange, Main.offScreenRange);
		Vector2 pos  = new Vector2(i * 16 - (int)Main.screenPosition.X,
		                           j * 16 - (int)Main.screenPosition.Y) + zero;
		Color light  = Lighting.GetColor(i, j);
		// Style2x2 quadrants at (0,0)/(Cell,0)/(0,Cell)/(Cell,Cell), each Up/2xUp/2.
		var src = new Rectangle(cornerX * Cell, cornerY * Cell, Up / 2, Up / 2);
		sb.Draw(sheet, pos, src, light, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
	}

	// Pre-upscaled to match the idle base (sampler-agnostic).
	private static readonly Dictionary<string, Texture2D?> _activeOverlays = new();

	private static Texture2D? GetActiveOverlay(string overlayDir, string overlayBasename)
	{
		string key = $"{overlayDir}/{overlayBasename}";
		if (_activeOverlays.TryGetValue(key, out var cached)) return cached;
		var raw = LoadOverlay(overlayDir, overlayBasename, active: true);
		var up  = raw is null ? null : UpscaleTexture2x(raw);
		_activeOverlays[key] = up;
		return up;
	}

	private static Texture2D UpscaleTexture2x(Texture2D src)
	{
		int w = src.Width, h = src.Height;
		var px = new Color[w * h];
		src.GetData(px);
		var up = new Color[w * 2 * h * 2];
		for (int y = 0; y < h * 2; y++)
			for (int x = 0; x < w * 2; x++)
				up[y * (w * 2) + x] = px[(y / 2) * w + (x / 2)];
		var tex = RuntimeTextureRegistry.New(w * 2, h * 2);
		tex.SetData(up);
		return tex;
	}

	// Transformer 2x2 face is split (HV top / LV bottom); both DOWN + UP
	// directions are pre-composited. DOWN bakes into the base sheet; UP is
	// drawn per-cell in PostDraw when the screwdriver flips it.
	private static Color[]? CompositeTransformerFace(VoltageTier tier, int baseAmp, bool isUp)
	{
		var casing16 = ReadFace(LoadCasing(Casing.Voltage, tier), srcY: 0);
		if (casing16 is null) return null;
		var art = Upscale2x(casing16);

		// HV face = baseAmp amps; LV face = baseAmp x 4. Direction flips IO.
		string hv = $"block/overlay/machine/overlay_energy_{baseAmp}a_{(isUp ? "out" : "in")}";
		string lv = $"block/overlay/machine/overlay_energy_{baseAmp * 4}a_{(isUp ? "in" : "out")}";
		CompositeFaceBand(art, hv, topHalf: true);
		CompositeFaceBand(art, lv, topHalf: false);
		return art;
	}

	// Overlay drawn at NATIVE 16x16 (not 2x upscale) - each band is only 16 px
	// tall, so a 2x icon would overflow.
	private static void CompositeFaceBand(Color[] art32, string overlayPath, bool topHalf)
	{
		var ov16 = ReadFace(LoadOptional($"{TexRoot}/{overlayPath}"), srcY: 0);
		if (ov16 is null) return;
		int dstY0 = topHalf ? 0 : Up / 2;
		const int xOff = (Up - Face) / 2;   // centre 16-wide in 32-wide band
		for (int y = 0; y < Face; y++)
		for (int x = 0; x < Face; x++)
		{
			var s = ov16[y * Face + x];
			if (s.A == 0) continue;
			int di = (dstY0 + y) * Up + (xOff + x);
			if (s.A == 255) { art32[di] = s; continue; }
			var d = art32[di];
			float sa = s.A / 255f, ia = 1f - sa;
			art32[di] = new Color(
				(byte)(s.R + d.R * ia), (byte)(s.G + d.G * ia),
				(byte)(s.B + d.B * ia), (byte)(s.A + d.A * ia));
		}
	}

	private static Texture2D MakeTexture(Color[] px, int w, int h)
	{
		var tex = RuntimeTextureRegistry.New(w, h);
		tex.SetData(px);
		return tex;
	}

	public static void EnsureTransformerTile(int tileType, VoltageTier tier, int baseAmp)
	{
		if (!_tilesDone.Add(tileType)) return;
		var art = CompositeTransformerFace(tier, baseAmp, isUp: false);
		if (art is null) return;
		TextureAssets.Tile[tileType] = WrapAsset(BuildSheetFrom32(art), $"gtceu_tile_{tileType}");
	}

	public static void EnsureTransformerItem(int itemType, VoltageTier tier, int baseAmp)
	{
		if (!_itemsDone.Add(itemType)) return;
		var art = CompositeTransformerFace(tier, baseAmp, isUp: false);
		if (art is null) return;
		TextureAssets.Item[itemType] = WrapAsset(MakeTexture(art, Up, Up), $"gtceu_item_{itemType}");
	}

	// UP art is opaque casing, fully covers the DOWN base.
	private static readonly Dictionary<string, Texture2D?> _transformerUp = new();

	public static void DrawTransformerUpFace(SpriteBatch sb, int i, int j,
	                                         VoltageTier tier, int baseAmp, int quadX, int quadY)
	{
		string key = $"{(int)tier}_{baseAmp}";
		if (!_transformerUp.TryGetValue(key, out var tex))
		{
			var art = CompositeTransformerFace(tier, baseAmp, isUp: true);
			tex = art is null ? null : MakeTexture(art, Up, Up);
			_transformerUp[key] = tex;
		}
		if (tex is null) return;

		Vector2 zero = Main.drawToScreen ? Vector2.Zero : new Vector2(Main.offScreenRange, Main.offScreenRange);
		Vector2 pos  = new Vector2(i * 16 - (int)Main.screenPosition.X,
		                           j * 16 - (int)Main.screenPosition.Y) + zero;
		Color light  = Lighting.GetColor(i, j);
		var src = new Rectangle(quadX * (Up / 2), quadY * (Up / 2), Up / 2, Up / 2);
		sb.Draw(tex, pos, src, light, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
	}

	// Layer order back->front: casing -> pipe -> tinted -> directional -> emissive
	// (mirrors upstream model JSON). Returns null if a required source is missing.
	private static Color[]? CompositeFace(IMachineTextureSpec spec, VoltageTier tier,
	                                       bool active, int frame,
	                                       Color[]? overrideCasingFace = null)
	{
		Color[]? result = null;

		if (overrideCasingFace == null && !string.IsNullOrEmpty(spec.CustomFaceAssetPath))
		{
			var customPx = ReadFaceResampled(LoadOptional(spec.CustomFaceAssetPath!));
			if (customPx is not null) return customPx;
		}

		if (overrideCasingFace != null)
		{
			// Fused-casing path - caller supplied the face (cleanroom->plascrete).
			result = (Color[])overrideCasingFace.Clone();
		}
		else if (!string.IsNullOrEmpty(spec.CustomCasingTexturePath))
		{
			// Multi controllers carry the appearance-block path (heatproof /
			// frostproof / watertight / etc.) so each multi renders with its
			// own casing instead of a tier-voltage one. Mirrors upstream's
			// `workableCasingModel(appearance, overlay)` first arg.
			var casingPx = ReadFace(LoadOptional($"{TexRoot}/{spec.CustomCasingTexturePath}"), srcY: 0);
			if (casingPx is null) return null;
			result = casingPx;
		}
		else if (spec.CasingKind != Casing.None)
		{
			var casingPx = ReadFace(LoadCasing(spec.CasingKind, tier), srcY: 0);
			if (casingPx is null) return null;
			result = casingPx;
		}

		// 1. Pipe overlay (no tint) - upstream `#overlay_pipe`.
		CompositeOverlay(ref result, spec.OverlayDir, spec.PipeOverlayBasename, tint: null);

		// 2. Tinted overlay (tier-color multiplied) - upstream `#overlay_tint`
		//    with `tintindex: 2`. The tier text color maps directly to MC's
		//    ChatFormatting hex, which is what `tintindex: 2` resolves to for
		//    GT machine models.
		CompositeOverlay(ref result, spec.OverlayDir, spec.TintedOverlayBasename,
			tint: VoltageTiers.TextColor(tier));

		// 3. Directional overlay - carries the _active animation.
		var directionalTex = LoadOverlay(spec.OverlayDir, spec.OverlayBasename, active);
		var directionalPx  = ReadFace(directionalTex, srcY: active ? frame * Face : 0);
		if (directionalPx is not null)
		{
			if (result is null) result = directionalPx;
			else AlphaCompositeOver(result, directionalPx);
		}

		// 4. Emissive overlay - baked at full brightness (no per-pixel vanilla
		// lighting). Darkness dims uniformly with the casing instead of staying lit.
		CompositeOverlay(ref result, spec.OverlayDir, spec.EmissiveOverlayBasename, tint: null);

		return result;
	}

	// MC's model UV is [0,16] regardless of source resolution, so 32x32 PNGs
	// (overlay_pipe_9x) downsample to 16. Animated strips stay; ReadFace
	// grabs the first frame.
	private static void CompositeOverlay(ref Color[]? result, string overlayDir,
	                                      string overlayBasename, Color? tint)
	{
		if (string.IsNullOrEmpty(overlayBasename)) return;
		var tex = LoadOptional($"{TexRoot}/{overlayDir}/{overlayBasename}");
		var px  = ReadFaceResampled(tex);
		if (px is null) return;
		if (tint is Color t) ApplyTint(px, t);
		if (result is null) result = px;
		else AlphaCompositeOver(result, px);
	}

	private static Color[]? ReadFaceResampled(Texture2D? tex)
	{
		if (tex is null) return null;
		if (tex.Width == Face) return ReadFace(tex, srcY: 0);
		if (tex.Width == tex.Height && tex.Width > Face && tex.Width % Face == 0)
			return BoxDownsampleToFace(tex);
		return ReadFace(tex, srcY: 0);
	}

	// Alpha-weighted RGB average; transparent pixels don't darken the mix.
	private static Color[] BoxDownsampleToFace(Texture2D src)
	{
		int n = src.Width;
		int block = n / Face;
		var raw = new Color[n * n];
		src.GetData(raw);
		var dst = new Color[Face * Face];
		for (int dy = 0; dy < Face; dy++)
		for (int dx = 0; dx < Face; dx++)
		{
			int aSum = 0, rSum = 0, gSum = 0, bSum = 0, weight = 0;
			int sx0 = dx * block, sy0 = dy * block;
			for (int yy = 0; yy < block; yy++)
			for (int xx = 0; xx < block; xx++)
			{
				var s = raw[(sy0 + yy) * n + (sx0 + xx)];
				aSum += s.A;
				rSum += s.R * s.A;
				gSum += s.G * s.A;
				bSum += s.B * s.A;
				weight++;
			}
			byte a = (byte)(aSum / weight);
			if (a == 0) { dst[dy * Face + dx] = default; continue; }
			dst[dy * Face + dx] = new Color((byte)(rSum / aSum), (byte)(gSum / aSum), (byte)(bSum / aSum), a);
		}
		return dst;
	}

	// MC tintindex semantics: RGB only, alpha untouched.
	private static void ApplyTint(Color[] px, Color tint)
	{
		for (int k = 0; k < px.Length; k++)
		{
			var s = px[k];
			if (s.A == 0) continue;
			px[k] = new Color(
				(byte)((s.R * tint.R) / 255),
				(byte)((s.G * tint.G) / 255),
				(byte)((s.B * tint.B) / 255),
				s.A);
		}
	}

	private static Color[]? ReadFace(Texture2D? tex, int srcY)
	{
		if (tex is null || tex.Width < Face || tex.Height < srcY + Face) return null;
		var all = new Color[tex.Width * tex.Height];
		tex.GetData(all);
		var face = new Color[Face * Face];
		for (int y = 0; y < Face; y++)
			for (int x = 0; x < Face; x++)
				face[y * Face + x] = all[(srcY + y) * tex.Width + x];
		return face;
	}

	// Straight-alpha src-over (tML PNGs are NOT premultiplied; the
	// premultiplied form over-brightens 0<a<255 pixels = halos on AA edges).
	private static void AlphaCompositeOver(Color[] dst, Color[] src)
	{
		for (int k = 0; k < dst.Length; k++)
		{
			var s = src[k];
			if (s.A == 0) continue;
			if (s.A == 255) { dst[k] = s; continue; }
			var d = dst[k];
			float sa = s.A / 255f, da = d.A / 255f;
			float ia = 1f - sa;
			float ao = sa + da * ia;
			if (ao <= 0f) { dst[k] = default; continue; }
			float inv = 1f / ao;
			dst[k] = new Color(
				(byte)((s.R * sa + d.R * da * ia) * inv),
				(byte)((s.G * sa + d.G * da * ia) * inv),
				(byte)((s.B * sa + d.B * da * ia) * inv),
				(byte)(ao * 255f));
		}
	}

	private static Color[] Upscale2x(Color[] face16)
	{
		var up = new Color[Up * Up];
		for (int y = 0; y < Up; y++)
			for (int x = 0; x < Up; x++)
				up[y * Up + x] = face16[(y / 2) * Face + (x / 2)];
		return up;
	}

	// Internal so MaterialBlockRenderer shares the layout math.
	internal static Texture2D BuildStyle2x2Sheet(Color[] face16) => BuildSheetFrom32(Upscale2x(face16));

	// Direct-32 path used by the Transformer's split face.
	internal static Texture2D BuildSheetFrom32(Color[] art32)
	{
		var sheet = new Color[36 * 36];
		for (int q = 0; q < 4; q++)
		{
			int qx = q % 2, qy = q / 2;
			for (int y = 0; y < Face; y++)
				for (int x = 0; x < Face; x++)
					sheet[(qy * Cell + y) * 36 + (qx * Cell + x)] = art32[(qy * Face + y) * Up + (qx * Face + x)];
		}
		var tex = RuntimeTextureRegistry.New(36, 36);
		tex.SetData(sheet);
		return tex;
	}

	// PNG round-trip once per machine (lazy) - negligible cost.
	internal static Asset<Texture2D> WrapAsset(Texture2D tex, string name)
	{
		using var ms = new MemoryStream();
		tex.SaveAsPng(ms, tex.Width, tex.Height);
		ms.Position = 0;
		var asset = Main.Assets.CreateUntracked<Texture2D>(ms, name + ".png");
		// CreateUntracked decodes a SEPARATE ReLogic-owned Texture2D (the copy that
		// lives in TextureAssets and is actually drawn). "Untracked" = ReLogic
		// won't dispose it, so we track it for Mod.Unload disposal - otherwise it
		// stays pinned in FNA's resource list and roots our ALC on reload. The
		// encode-source `tex` is already tracked (its caller built it via New()).
		RuntimeTextureRegistry.Track(asset.Value);
		return asset;
	}

	private const string TexRoot = "GregTechCEuTerraria/Content/Textures";

	private static Texture2D? LoadCasing(Casing casing, VoltageTier tier)
	{
		string? path = casing switch
		{
			Casing.None          => null,
			Casing.Voltage       => $"{TexRoot}/block/casings/voltage/{VoltageTiers.ShortName(tier).ToLowerInvariant()}/side",
			// Plain-brick face from casings/steam/bricked_*. The previous
			// firebox/machine_casing_firebox_bronze mapping baked a hatch
			// behind every machine - not what upstream does.
			Casing.BrickedBronze => $"{TexRoot}/block/casings/steam/bricked_bronze/side",
			Casing.BrickedSteel  => $"{TexRoot}/block/casings/steam/bricked_steel/side",
			Casing.CokeBricks    => $"{TexRoot}/block/casings/solid/machine_coke_bricks",
			Casing.Firebricks    => $"{TexRoot}/block/casings/solid/machine_primitive_bricks",
			Casing.PumpDeck      => $"{TexRoot}/block/casings/pump_deck/top",
			_                    => throw new ArgumentOutOfRangeException(nameof(casing), casing, null),
		};
		return path is null ? null : LoadOptional(path);
	}

	private static Texture2D? LoadOverlay(string overlayDir, string overlayBasename, bool active)
	{
		// Empty basename = casing alone (e.g. coke_oven_hatch).
		if (string.IsNullOrEmpty(overlayBasename)) return null;
		string name = active ? $"{overlayBasename}_active" : overlayBasename;
		var primary = LoadOptional($"{TexRoot}/{overlayDir}/{name}");
		if (primary is not null) return primary;
		if (active) return LoadOptional($"{TexRoot}/{overlayDir}/{overlayBasename}");
		return null;
	}

	private static Texture2D? LoadOptional(string assetPath)
	{
		if (!ModContent.HasAsset(assetPath)) return null;
		var asset = ModContent.Request<Texture2D>(assetPath, AssetRequestMode.ImmediateLoad);
		return asset?.Value;
	}
}
