#nullable enable
using GregTechCEuTerraria.Common.Materials;
using GregTechCEuTerraria.Api.Data.Chemical.Material;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles;

// Placeable material block - 2x2 solid tile, one ModTile per material block
// item: the storage block (`<material>_block`) and the raw-ore block
// (`raw_<material>_block`). `MaterialBlockRenderer` pre-bakes that block's
// registry-dump render layers (each tinted by its getLayerARGB value) into a
// cached Texture2D - the same layer set the block item and upstream draw -
// then vanilla samples it with the Lighting tint.
//
// Keyed by the upstream item id (not the material) so a material can own both
// its storage block and its raw-ore block. Sentinel pattern: parameterless
// ctor is the autoload probe (skipped via IsLoadingEnabled=false); real
// instances are added by MaterialBlockTileRegistry from Mod.Load.
public sealed class MaterialBlockTile : ModTile, ITextureWarmUp
{
	private readonly string? _id;
	private readonly Material? _material;
	private readonly bool _walkThrough;     // frames: walk-through + stand-on-top

	public MaterialBlockTile() { }
	public MaterialBlockTile(string id, Material material, bool walkThrough = false)
	{
		_id = id;
		_material = material;
		_walkThrough = walkThrough;
	}

	public override bool IsLoadingEnabled(Mod mod) => _id != null && _material != null;
	public override string Name => _id ?? nameof(MaterialBlockTile);
	// Placeholder asset - PreDraw bypasses default draw. See TieredMachineTile.cs.
	public override string Texture => "GregTechCEuTerraria/Content/TerrariaCompat/TooManyItemsTile";

	public override void SetStaticDefaults()
	{
		// Storage / raw-ore blocks are fully solid (NO tileSolidTop - that's for
		// half-platforms; on top of tileSolid it makes the player land-on-top
		// but pass through from the side). Frames are walk-through with a
		// stand-on-top surface, same collision as the GT machines / casings.
		Main.tileFrameImportant[Type] = true;
		Main.tileSolid[Type]          = !_walkThrough;
		Main.tileSolidTop[Type]       = _walkThrough;
		Main.tileBlockLight[Type]     = !_walkThrough;
		Main.tileLavaDeath[Type]      = false;

		// 2x2 placeable footprint. Origin (1, 1) puts the cursor on the
		// bottom-right cell of the placed multi-tile; the machine extends up
		// + left from the cursor, so the geometric center of the 2x2 sits a
		// half-tile up-left of cursor - closest cell-aligned approximation
		// to "centered on cursor" for an even-sided footprint. Matches
		// MetaMachineTile / CasingTile / TooManyItemsTile (consistent UX).
		TileObjectData.newTile.CopyFrom(TileObjectData.Style2x2);
		TileObjectData.newTile.LavaDeath    = false;
		TileObjectData.newTile.Origin       = new Point16(1, 1);
		TileObjectData.newTile.AnchorBottom = default(AnchorData);
		TileObjectData.addTile(Type);
		Players.CenteredPlacementPlayer.CenteredPlacementTiles.Add(Type);

		// Map dot tinted to material color so the minimap shows blocks
		// distinctly. Falls back to dull grey if Color is unset.
		var mapColor = MaterialColor();
		AddMapEntry(mapColor,
			Language.GetOrRegister($"Mods.GregTechCEuTerraria.Tiles.{Name}.MapEntry",
				() => _id != null ? Humanize(_id) : "Material Block"));

		DustType = DustID.Iron;
		HitSound = SoundID.Tink;
		MineResist = 1f;
	}

	public override bool PreDraw(int i, int j, SpriteBatch spriteBatch)
	{
		// Lazy-install the composited texture into TextureAssets, then let vanilla
		// draw it (pixel-perfect, same texture the placement ghost + minimap read).
		// TextureWarmUpSystem normally installs this on the first frame; this call
		// is the idempotent safety net.
		WarmUpTexture();
		return true;
	}

	// Eager load-time install - TextureWarmUpSystem calls this on the first
	// frame so the placement ghost / minimap never flash the placeholder.
	public void WarmUpTexture()
	{
		if (_id is not null)
			MaterialBlockRenderer.EnsureTileTexture(Type, _id);
	}

	// Map dot color, also used as the dust tint while mining.
	private Color MaterialColor()
	{
		uint c = _material?.Color ?? 0xCCCCCCu;
		return new Color((byte)((c >> 16) & 0xFF), (byte)((c >> 8) & 0xFF), (byte)(c & 0xFF));
	}

	// "raw_aluminium_block" -> "Raw Aluminium Block" - minimap-hover fallback
	// only; the real label comes from the generated locale.
	private static string Humanize(string id)
	{
		var sb = new System.Text.StringBuilder(id.Length);
		bool capNext = true;
		foreach (char c in id)
		{
			if (c == '_') { sb.Append(' '); capNext = true; continue; }
			sb.Append(capNext ? char.ToUpperInvariant(c) : c);
			capNext = false;
		}
		return sb.ToString();
	}
}
