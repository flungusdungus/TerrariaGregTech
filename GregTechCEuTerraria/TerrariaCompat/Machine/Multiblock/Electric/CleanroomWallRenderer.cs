#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Electric;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Electric;

// Plascrete "fake walls" behind formed cleanroom interiors. Pure visual -
// no WallType mutation. Hooks DoDraw_WallsAndBlacks (CableLayerSystem
// pattern), so plascrete draws after vanilla walls + before solid tiles -
// any block placed inside covers naturally.
//
// Source: plascrete tile's TextureAssets.Tile sheet (CasingRenderer's
// 36x36 Style2x2 face). Quadrants at (0,0), (18,0), (0,18), (18,18) give
// adjacent tiles varied sub-regions. Tinted Lighting.GetColor x WallDimming.
public sealed class CleanroomWallRenderer : ModSystem
{
	// 0.55 keeps plascrete readable while reading "behind me" vs foreground.
	private const float WallDimming = 0.55f;

	private static int _plascreteTileType = -1;

	public override void Load()
	{
		if (Main.dedServ) return;
		On_Main.DoDraw_WallsAndBlacks += DrawCleanroomWallsAfter;
	}

	public override void Unload()
	{
		if (Main.dedServ) return;
		On_Main.DoDraw_WallsAndBlacks -= DrawCleanroomWallsAfter;
		_plascreteTileType = -1;
	}

	private static void DrawCleanroomWallsAfter(On_Main.orig_DoDraw_WallsAndBlacks orig, Main self)
	{
		orig(self);
		Draw(Main.spriteBatch);
	}

	private static void Draw(SpriteBatch sb)
	{
		// Lazy resolution - CasingRegistry hasn't run when Load fires.
		if (_plascreteTileType < 0)
		{
			var mod = ModLoader.GetMod("GregTechCEuTerraria");
			if (mod.TryFind<ModTile>("plascrete", out var t)) _plascreteTileType = t.Type;
			else return;
		}
		var tex = TextureAssets.Tile[_plascreteTileType]?.Value;
		if (tex == null) return;

		// +1 margin matches the cable renderer.
		int firstX = (int)(Main.screenPosition.X / 16) - 1;
		int lastX  = (int)((Main.screenPosition.X + Main.screenWidth) / 16) + 1;
		int firstY = (int)(Main.screenPosition.Y / 16) - 1;
		int lastY  = (int)((Main.screenPosition.Y + Main.screenHeight) / 16) + 1;

		foreach (var te in TileEntity.ByID.Values)
		{
			if (te is not CleanroomMachine cm) continue;
			if (!cm.IsFormed || cm.FormedTileWidth <= 0) continue;

			int x0 = cm.FormedTopLeft.X;
			int y0 = cm.FormedTopLeft.Y;
			int x1 = x0 + cm.FormedTileWidth;
			int y1 = y0 + cm.FormedTileHeight;
			if (x1 < firstX || x0 > lastX || y1 < firstY || y0 > lastY) continue;

			// Interior = full rect minus perimeter cell-row/col (2 tiles/cell).
			int ix0 = System.Math.Max(x0 + 2, firstX);
			int iy0 = System.Math.Max(y0 + 2, firstY);
			int ix1 = System.Math.Min(x1 - 2, lastX);   // exclusive
			int iy1 = System.Math.Min(y1 - 2, lastY);

			for (int y = iy0; y < iy1; y++)
			for (int x = ix0; x < ix1; x++)
			{
				var tile = Main.tile[x, y];
				if (tile.HasTile) continue;
				int qx = (x & 1) * 18;
				int qy = (y & 1) * 18;
				var src = new Rectangle(qx, qy, 16, 16);
				var light = Lighting.GetColor(x, y) * WallDimming;
				var pos = new Vector2(x * 16 - Main.screenPosition.X,
				                      y * 16 - Main.screenPosition.Y);
				sb.Draw(tex, pos, src, light);
			}
		}
	}
}
