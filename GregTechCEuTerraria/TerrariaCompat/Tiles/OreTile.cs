#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Common.Materials;
using GregTechCEuTerraria.Api.Data.Chemical.Material;
using GregTechCEuTerraria.TerrariaCompat.Items;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles;

// One ModTile instance per Material with ORE form. Same sentinel pattern as
// MaterialItem: parameterless ctor for tML's autoload probe, real ctor for
// OreTileRegistry-style manual AddContent.
//
// Texture sheet contains only the per-material tinted mineral on a transparent
// background (see tools/scripts/generate-ore-tiles.py). PreDraw layers vanilla
// stone underneath using the player's installed game textures so the stone
// matches actual Terraria art exactly.
public sealed class OreTile : ModTile
{
	private readonly Material? _material;

	// Logs only the very first PreDraw fallback so we know something failed.
	private static bool _preDrawFallbackLogged;

	public override string Name => _material != null ? $"{_material.Id}_ore" : nameof(OreTile);

	// Placeholder asset - PreDraw composites the upstream vein-shape with
	// material-color tint at draw time. Use vanilla stone as a stand-in that
	// reliably exists (size doesn't matter - never sampled directly).
	public override string Texture => "Terraria/Images/Tiles_1";

	public override bool IsLoadingEnabled(Mod mod) => _material != null;

	public OreTile() { }

	public OreTile(Material material) { _material = material; }

	private static readonly ushort[] MergeWith =
	{
		TileID.Stone, TileID.Dirt, TileID.Mud, TileID.ClayBlock,
		TileID.Sand, TileID.HardenedSand, TileID.Sandstone,
		TileID.SnowBlock, TileID.IceBlock, TileID.Slush, TileID.Silt,
		TileID.Granite, TileID.Marble,
		TileID.Ebonstone, TileID.Crimstone, TileID.Pearlstone,
		TileID.Ash, TileID.Hellstone,
	};

	public override void SetStaticDefaults()
	{
		if (_material == null) return;

		TileID.Sets.Ore[Type] = true;
		Main.tileSpelunker[Type] = true;
		Main.tileOreFinderPriority[Type] = 410;
		Main.tileShine2[Type] = true;
		Main.tileShine[Type] = 975;
		Main.tileMergeDirt[Type] = true;
		Main.tileSolid[Type] = true;
		Main.tileBlockLight[Type] = true;

		foreach (ushort other in MergeWith)
		{
			Main.tileMerge[Type][other] = true;
			Main.tileMerge[other][Type] = true;
		}

		DustType = DustID.Platinum;
		HitSound = SoundID.Tink;

		uint c = _material.Color ?? 0xAAAAAA;
		var mapColor = new Color((byte)((c >> 16) & 0xFF), (byte)((c >> 8) & 0xFF), (byte)(c & 0xFF));
		string mapEntryKey = $"Mods.GregTechCEuTerraria.Tiles.{_material.Id}_ore.MapEntry";
		string materialNameKey = $"Mods.GregTechCEuTerraria.Materials.{_material.Id}";
		var name = Language.GetOrRegister(mapEntryKey,
			() => $"{Language.GetTextValue(materialNameKey)} Ore");
		AddMapEntry(mapColor, name);
	}

	// Texture is "Terraria/Images/Tiles_1" (vanilla Stone sheet) and our tile's
	// auto-tile frame coords are computed against that sheet - so vanilla's
	// DrawSingleTile renders the stone background for us correctly. We let it
	// run (PreDraw returns true) so vanilla's sparkle particles + spelunker /
	// dangersense / biome-sight tints fire normally, and we overlay the
	// material-tinted vein on top in PostDraw.
	public override void PostDraw(int i, int j, SpriteBatch spriteBatch)
	{
		if (_material == null) return;
		var veinAsset = OreRenderer.GetVeinAsset(_material.IconSet);
		if (veinAsset?.Value == null)
		{
			if (!_preDrawFallbackLogged)
			{
				_preDrawFallbackLogged = true;
				ModContent.GetInstance<GregTechCEuTerraria>().Logger.Warn(
					$"[OreTile.PostDraw] vein texture not loaded for {_material.Id} ({_material.IconSet})");
			}
			return;
		}

		Vector2 zero = Main.drawToScreen ? Vector2.Zero : new Vector2(Main.offScreenRange, Main.offScreenRange);
		Vector2 pos = new Vector2(i * 16 - (int)Main.screenPosition.X, j * 16 - (int)Main.screenPosition.Y) + zero;
		var veinFrame = new Rectangle(0, 0, 16, 16);
		Color light = Lighting.GetColor(i, j);
		// Mirror vanilla DrawSingleTile's tileLight bumps (TileDrawing.cs:885-967)
		// so spelunker / dangersense make the vein overlay visible too - Vanilla
		// applies these to the *internal* tileLight before its own draw, but
		// Lighting.GetColor here returns the raw cave light, so we redo them.
		var p = Main.LocalPlayer;
		if (p.findTreasure && Main.IsTileSpelunkable(i, j))
		{
			if (light.R < 200) light.R = 200;
			if (light.G < 170) light.G = 170;
		}
		if (p.dangerSense)
		{
			if (light.R < byte.MaxValue) light.R = byte.MaxValue;
			if (light.G < 50) light.G = 50;
			if (light.B < 50) light.B = 50;
		}
		Color tint = MaterialColor();
		spriteBatch.Draw(veinAsset.Value, pos, veinFrame, OreRenderer.MultiplyRGB(light, tint));
	}

	private Color MaterialColor()
	{
		uint c = _material?.Color ?? 0xAAAAAAu;
		return new Color((byte)((c >> 16) & 0xFF), (byte)((c >> 8) & 0xFF), (byte)(c & 0xFF));
	}

	public override IEnumerable<Item> GetItemDrops(int i, int j)
	{
		if (_material == null) yield break;
		int rawOreType = MaterialItemRegistry.Get(_material.Id, "raw_ore") ?? 0;
		if (rawOreType > 0)
			yield return new Item(rawOreType) { stack = OreTileRegistry.RawOrePerBlock };
	}
}
