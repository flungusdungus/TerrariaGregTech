#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles;

// Builds the "Too Many Items" art from the upstream GregTech logo
// (Content/Textures/gui/icon/gregtech_logo, 17x17): a dark tech-plate with the
// logo composited dead-centre. Used for the placeable tile, its item icon, and
// the inventory button - replacing the old magenta/black 404 placeholder.
//
// The 32x32 plate is baked once; the tile gets it sliced into a Style2x2
// 36x36 sheet, the item gets it 1:1, the button draws the raw plate texture.
internal static class TooManyItemsArt
{
	private const string LogoPath = "GregTechCEuTerraria/Content/Textures/gui/icon/gregtech_logo";
	private static readonly Color Plate  = new(38, 42, 70);
	private static readonly Color Border = new(90, 100, 140);

	private static Color[]? _art;          // cached 32x32 plate+logo
	private static Texture2D? _plateTex;   // cached GPU texture for the button
	private static bool _installed;

	// 32x32 plate+logo texture - drawn by the inventory button. Null only if
	// the logo asset failed to load.
	public static Texture2D? PlateTexture
	{
		get
		{
			if (_plateTex != null) return _plateTex;
			var art = Build32();
			if (art is null) return null;
			_plateTex = RuntimeTextureRegistry.New(32, 32);
			_plateTex.SetData(art);
			return _plateTex;
		}
	}

	// Replace the autoloaded item icon + tile sheet with the logo plate.
	// Idempotent. MUST run on the main thread (graphics calls) - invoked via
	// the ITextureWarmUp pass on TooManyItemsTile / TooManyItemsItem.
	public static void Install()
	{
		if (_installed || Main.dedServ) return;

		var art = Build32();
		if (art is null) return;   // logo not ready - retry on the next warm-up call
		_installed = true;

		int itemType  = ModContent.ItemType<Items.TooManyItemsItem>();
		int tileType  = ModContent.TileType<TooManyItemsTile>();

		var itemTex = RuntimeTextureRegistry.New(32, 32);
		itemTex.SetData(art);
		var itemAsset = MachineRenderer.WrapAsset(itemTex, "tmi_item");
		TextureAssets.Item[itemType]  = itemAsset;

		// Slice the 32x32 plate into the Style2x2 36x36 tile sheet.
		var sheet = MachineRenderer.BuildSheetFrom32(art);
		TextureAssets.Tile[tileType] = MachineRenderer.WrapAsset(sheet, "tmi_tile");
	}

	// 32x32: dark plate + 1px border + the 17x17 logo composited centred.
	private static Color[]? Build32()
	{
		if (_art != null) return _art;

		Texture2D logo;
		try { logo = ModContent.Request<Texture2D>(LogoPath, AssetRequestMode.ImmediateLoad).Value; }
		catch { return null; }

		var art = new Color[32 * 32];
		for (int i = 0; i < art.Length; i++) art[i] = Plate;
		for (int x = 0; x < 32; x++) { art[x] = Border; art[31 * 32 + x] = Border; }
		for (int y = 0; y < 32; y++) { art[y * 32] = Border; art[y * 32 + 31] = Border; }

		int lw = logo.Width, lh = logo.Height;
		var logoPx = new Color[lw * lh];
		logo.GetData(logoPx);
		int ox = (32 - lw) / 2, oy = (32 - lh) / 2;
		for (int y = 0; y < lh; y++)
			for (int x = 0; x < lw; x++)
			{
				Color src = logoPx[y * lw + x];
				if (src.A == 0) continue;
				int di = (oy + y) * 32 + (ox + x);
				// Straight-alpha src-over onto the opaque plate (tML loads
				// PNGs as straight RGBA - see ImageIO.ReadRaw). The
				// premultiplied form (Cs + Cd*(1-As)) over-brightens edges
				// with partial alpha.
				float sa = src.A / 255f, ia = 1f - sa;
				Color dst = art[di];
				art[di] = new Color(
					(byte)(src.R * sa + dst.R * ia),
					(byte)(src.G * sa + dst.G * ia),
					(byte)(src.B * sa + dst.B * ia),
					(byte)255);
			}

		_art = art;
		return art;
	}
}
