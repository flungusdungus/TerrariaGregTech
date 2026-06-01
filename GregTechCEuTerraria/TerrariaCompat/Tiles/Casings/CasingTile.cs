#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Items.Registry;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Casings;

// Placeable casing / decorative block - a plain 2x2 tile with NO logic, one
// ModTile per gtceu cube BlockItem (dump-driven, see CasingRegistry). Casings
// are crafting ingredients, multiblock parts and decoration.
//
// Active-aware casings (those with `ActiveBlockTexture` in the registry dump
// - crushing_wheels, heating coils, fusion casings, ...) wear a secondary face
// while bound to a running multiblock. The face swap is driven by
// ActiveCasingState: the owning MultiblockControllerMachine writes the
// formed-and-active footprint cells into that set on form/active-edge, and
// CasingTile.PostDraw reads it per cell to overlay the active sheet.
//
// Collision matches the GT machines: walk-through, stand-on-top
// (tileSolidTop, NOT tileSolid). Texture is the upstream block face baked into
// a Style2x2 sheet by CasingRenderer. Sentinel pattern: parameterless ctor is
// the autoload probe (skipped via IsLoadingEnabled); real instances are added
// by CasingRegistry from Mod.Load.
public sealed class CasingTile : ModTile, ITextureWarmUp
{
	private readonly string? _id;
	private readonly string? _texture;          // gtceu-relative block texture path
	private readonly string? _activeTexture;    // optional active-state face (null if none)
	private readonly string? _displayName;

	public CasingTile() { }
	public CasingTile(string id, string texture, string displayName, string? activeTexture = null)
	{
		_id = id;
		_texture = texture;
		_activeTexture = activeTexture;
		_displayName = displayName;
	}

	public override bool IsLoadingEnabled(Mod mod) => _id != null;
	public override string Name => _id ?? nameof(CasingTile);
	// Placeholder asset - WarmUpTexture overrides TextureAssets.Tile. See MaterialBlockTile.
	public override string Texture => "GregTechCEuTerraria/Content/TerrariaCompat/TooManyItemsTile";

	// True when this casing has an active-state secondary face - used by
	// MultiblockControllerMachine to short-circuit the active-cell scan
	// (a multi made entirely of non-active-aware casings doesn't need the
	// per-active-edge bookkeeping).
	public bool IsActiveAware => _activeTexture != null;

	// The gtceu-relative block texture path (e.g. "block/casings/solid/
	// machine_casing_inert_ptfe") this casing renders. Exposed so multiblock
	// controllers that reskin bound parts to this casing don't need to
	// hand-mirror the path on their MachineDefinition - `MultiblockControllerMachine.
	// FusedCasingTexture` falls back to this when `Definition.FusedCasingTexturePath`
	// is null, so a definition can declare ONLY `FusedCasingTileName` and the
	// texture is derived automatically. Single source of truth.
	public string? BlockTexture => _texture;

	public override void SetStaticDefaults()
	{
		// Walk-through with a stand-on-top surface - same as the GT machines
		// (tileSolidTop only; tileSolid would block movement from the sides).
		Main.tileFrameImportant[Type] = true;
		Main.tileSolid[Type]          = false;
		Main.tileSolidTop[Type]       = true;
		Main.tileNoAttach[Type]       = false;
		Main.tileLavaDeath[Type]      = false;
		Main.tileBlockLight[Type]     = false;
		// Coils + firebox casings emit warm light while their owning multi is
		// formed AND working - see ModifyLight below. The flag is per-Type so
		// it has to be set regardless of which casing kind this is; the actual
		// emission gate happens at draw time.
		Main.tileLighted[Type]        = true;

		// 2x2 footprint, free placement (no solid-ground anchor - casings are
		// placed in mid-air as multiblock parts). Origin (1, 1) - cursor at
		// bottom-right cell of the placed multi-tile, machine extends up +
		// left; the visual center of the 2x2 sits a half-tile up-left of
		// cursor. Matches MetaMachineTile / MaterialBlockTile (consistent UX).
		TileObjectData.newTile.CopyFrom(TileObjectData.Style2x2);
		TileObjectData.newTile.LavaDeath    = false;
		TileObjectData.newTile.Origin       = new Point16(1, 1);
		TileObjectData.newTile.AnchorBottom = default(AnchorData);
		TileObjectData.addTile(Type);
		TerrariaCompat.Players.CenteredPlacementPlayer.CenteredPlacementTiles.Add(Type);

		AddMapEntry(new Color(120, 122, 134),
			Language.GetOrRegister($"Mods.GregTechCEuTerraria.Tiles.{Name}.MapEntry",
				() => _displayName ?? RegistryDump.Humanize(Name)));

		DustType = DustID.Iron;
		HitSound = SoundID.Tink;
		MineResist = 1.5f;
		MinPick = 0;
	}

	// Light emission for casings inside an active (formed + working) multi.
	// Looks up the casing's id in `MultiActiveLight` to pick an intensity:
	//   - heating coils -> tier-scaled bright orange (cupronickel->tritanium)
	//   - firebox casings -> moderate orange (forge fire)
	//   - other casings -> no glow (the controller block itself already emits
	//     the half-torch via MetaMachineTile.ModifyLight, so the rest of the
	//     wall stays dark unless it's a heat-emitting element)
	// Sub-cell positions of the 2x2 footprint resolve back to the anchor via
	// TileFrame{X,Y} so the active-state lookup uses the same key the
	// controller writes.
	public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
	{
		if (_id is null) return;
		var col = MultiActiveLight.For(_id);
		if (col.X == 0f && col.Y == 0f && col.Z == 0f) return;

		Tile tile = Main.tile[i, j];
		int anchorX = i - (tile.TileFrameX >= 18 ? 1 : 0);
		int anchorY = j - (tile.TileFrameY >= 18 ? 1 : 0);
		if (!ActiveCasingState.IsActive(anchorX, anchorY)) return;

		r += col.X;
		g += col.Y;
		b += col.Z;
	}

	public override bool PreDraw(int i, int j, SpriteBatch spriteBatch)
	{
		// Lazy install - TextureWarmUpSystem normally does this on the first
		// frame; this is the idempotent safety net.
		WarmUpTexture();
		return true;
	}

	// Active-aware casings overlay their `_activeTexture` sheet on top of the
	// base face when the cell is registered in ActiveCasingState (= the owning
	// multiblock is formed AND running). Each casing tile is 2x2 cells; the
	// cell anchor (top-left of the 2x2 footprint) is recovered from TileFrame.
	public override void PostDraw(int i, int j, SpriteBatch spriteBatch)
	{
		if (_activeTexture is null) return;

		Tile tile = Main.tile[i, j];
		// Map sub-cell back to the 2x2 anchor - TileFrame{X,Y} are 0 or 18
		// within a Style2x2 footprint, corresponding to a tile offset of 0/1.
		int anchorX = i - (tile.TileFrameX >= 18 ? 1 : 0);
		int anchorY = j - (tile.TileFrameY >= 18 ? 1 : 0);
		if (!ActiveCasingState.IsActive(anchorX, anchorY)) return;

		var sheet = CasingRenderer.GetActiveSheet(Type);
		if (sheet is null) return;

		// Same blit math vanilla uses for a Style2x2 tile face - 16x16 source
		// at (TileFrameX, TileFrameY), 16x16 destination at the on-screen tile
		// position. Lighting is sampled per-cell so the overlay dims with the
		// world; if you want emissive-style glow swap to `Color.White` here.
		Vector2 zero = Main.drawToScreen ? Vector2.Zero
		                                 : new Vector2(Main.offScreenRange, Main.offScreenRange);
		Vector2 pos  = new Vector2(i * 16 - (int)Main.screenPosition.X,
		                           j * 16 - (int)Main.screenPosition.Y) + zero;
		var src   = new Rectangle(tile.TileFrameX, tile.TileFrameY, 16, 16);
		var light = Lighting.GetColor(i, j);
		spriteBatch.Draw(sheet, pos, src, light, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
	}

	// Eager load-time install - TextureWarmUpSystem calls this on the first
	// frame so the placement ghost / minimap never flash the placeholder.
	public void WarmUpTexture()
	{
		if (_texture is not null)
			CasingRenderer.EnsureTileTexture(Type, _texture, _activeTexture);
	}
}
