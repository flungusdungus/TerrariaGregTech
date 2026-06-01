#nullable enable
using System;
using GregTechCEuTerraria.Api.Pattern;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using ReLogic.Content;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;

// 2D analogue of MultiblockInWorldPreviewRenderer. Draws ghost icons over
// every unformed multi's shape cells. RepeatableBlockPattern previews the
// max-N shape via GetPreviewPattern.
// Item icons replace upstream's BlockState tessellation (no Terraria
// equivalent for arbitrary tiles). Always-on (user requirement); flat 50%
// alpha; skips IsAny/IsAir/IsController cells.
public static class MultiblockPreviewRenderer
{
	private static readonly Color GhostTint = new Color(255, 255, 255, 128) * 0.5f;

	// Pulsing red for the resolved error cell ("Wrong block at (X, Y)").
	private static Color ErrorTint()
	{
		float pulse = 0.5f + 0.5f * (float)Math.Sin(Main.GameUpdateCount * 0.12f);
		return new Color(255, 70, 70, 160) * (0.55f + 0.25f * pulse);
	}

	// 1 shape cell = 2x2 tiles = 32x32 px at native scale.
	private const int CellPx = 32;

	// Per-Draw scratch (reused). Avoids O(TileEntity.ByPosition) per cell.
	private static readonly System.Collections.Generic.List<Microsoft.Xna.Framework.Rectangle> _formedFootprints = new();

	// PostDrawTiles - paint above all tiles so later tile-pass writes don't occlude.
	public static void DrawAll(SpriteBatch sb)
	{
		var screenRect = new Microsoft.Xna.Framework.Rectangle(
			(int)Main.screenPosition.X, (int)Main.screenPosition.Y,
			Main.screenWidth, Main.screenHeight);

		foreach (var (_, te) in Terraria.DataStructures.TileEntity.ByID)
		{
			if (te is not MultiblockControllerMachine c || c.IsFormed) continue;
			if (!Multiblock.MultiblockPreviewHover.IsControllerTileAlive(c)) continue;

			var pattern = c.GetPattern();
			if (pattern is null) continue;
			var preview = pattern.GetPreviewPattern();
			int ox = c.Position.X - preview.ControllerCol * 2;
			int oy = c.Position.Y - preview.ControllerRow * 2;
			var px = new Microsoft.Xna.Framework.Rectangle(
				ox * 16, oy * 16, preview.Width * 32, preview.Height * 32);
			if (!px.Intersects(screenRect)) continue;

			Draw(c, sb);
		}
	}

	// GameViewMatrix space: worldPx - screenPosition (matrix handles target offset).
	public static void Draw(MultiblockControllerMachine controller, SpriteBatch sb)
	{
		if (controller.IsFormed) return;
		var pattern = controller.GetPattern();
		if (pattern is null) return;

		var preview = pattern.GetPreviewPattern();
		int originX = controller.Position.X - preview.ControllerCol * 2;
		int originY = controller.Position.Y - preview.ControllerRow * 2;

		// Resolved error cell ("Wrong block at (X, Y)") -> red tint.
		var errorCell = controller.GetUnformedErrorCell();

		// "Not enough <part>": flag casing cells that could host the missing
		// part with red highlight (swap-hint).
		var swapList = controller.GetSwapCandidateTypes();
		System.Collections.Generic.HashSet<int>? swapTypes = null;
		if (swapList is { Count: > 0 })
			swapTypes = new System.Collections.Generic.HashSet<int>(swapList);

		// Gather formed-multi footprints once; per-cell test walks this list
		// instead of TileEntity.ByPosition. Bounds-filtered.
		_formedFootprints.Clear();
		var previewBounds = new Microsoft.Xna.Framework.Rectangle(
			originX, originY, preview.Width * 2, preview.Height * 2);
		Multiblock.MultiblockPreviewHover.GatherFormedFootprintsOverlapping(
			previewBounds, _formedFootprints);

		for (int row = 0; row < preview.Height; row++)
		{
			for (int col = 0; col < preview.Width; col++)
			{
				char ch = preview.Shape[row][col];
				if (!preview.Predicates.TryGetValue(ch, out var predicate))
					continue;
				if (predicate.IsAny() || predicate.IsAir() || predicate.IsController)
					continue;

				var item = predicate.PreviewItem();
				if (item is null || item.type == ItemID.None) continue;

				int tileX = originX + col * 2;
				int tileY = originY + row * 2;

				// Symmetric with MultiblockPreviewHover.TryFind - formed multis claim their footprint.
				if (IsInsideAnyFootprint(_formedFootprints, tileX, tileY))
					continue;

				// Skip ghost on already-correct cells (visual optimization).
				if (Multiblock.MultiblockPreviewHover.PredicateMatchesTileAt(predicate, tileX, tileY))
				{
					// ...unless the cell could host a missing part - red box hints
					// "replace this casing with the part".
					if (swapTypes is not null
						&& CellAcceptsAnyType(predicate, swapTypes)
						&& !swapTypes.Contains(Multiblock.MultiblockPreviewHover.PlacedSiblingItemType(tileX, tileY)))
					{
						Vector2 swapPos = new Vector2(tileX * 16, tileY * 16) - Main.screenPosition;
						DrawCellHighlight(sb, swapPos, ErrorTint());
					}
					continue;
				}

				Vector2 screenPos = new Vector2(tileX * 16, tileY * 16) - Main.screenPosition;
				bool isErrorCell = errorCell is { } ec && ec.X == tileX && ec.Y == tileY;
				DrawGhostItem(sb, item, screenPos, isErrorCell ? ErrorTint() : GhostTint);
			}
		}
	}

	// Does this predicate accept any of these item types (= could host the part)?
	private static bool CellAcceptsAnyType(Api.Pattern.TraceabilityPredicate p, System.Collections.Generic.HashSet<int> types)
	{
		return Scan(p.Common) || Scan(p.Limited);

		bool Scan(System.Collections.Generic.List<Api.Pattern.SimplePredicate> bucket)
		{
			foreach (var sp in bucket)
			{
				var cand = sp.Candidates?.Invoke();
				if (cand is null) continue;
				foreach (var it in cand)
					if (it is not null && !it.IsAir && types.Contains(it.type))
						return true;
			}
			return false;
		}
	}

	private static bool IsInsideAnyFootprint(System.Collections.Generic.List<Microsoft.Xna.Framework.Rectangle> rects, int tileX, int tileY)
	{
		for (int i = 0; i < rects.Count; i++)
		{
			var r = rects[i];
			if (tileX >= r.X && tileX < r.X + r.Width && tileY >= r.Y && tileY < r.Y + r.Height)
				return true;
		}
		return false;
	}

	// Translucent box (NOT the part's icon) so it reads as a highlight, not
	// a "part already installed" preview.
	private static void DrawCellHighlight(SpriteBatch sb, Vector2 topLeft, Color color)
	{
		var px = TextureAssets.MagicPixel.Value;
		if (px is null) return;
		sb.Draw(px, new Rectangle((int)topLeft.X, (int)topLeft.Y, CellPx, CellPx),
			new Rectangle(0, 0, 1, 1), color);
	}

	private static void DrawGhostItem(SpriteBatch sb, Item item, Vector2 topLeft, Color tint)
	{
		Main.instance.LoadItem(item.type);
		var asset = TextureAssets.Item[item.type];
		if (asset is null) return;
		Texture2D tex;
		try { tex = asset.Value; }
		catch { return; }
		if (tex is null) return;

		Rectangle src;
		if (Main.itemAnimations[item.type] is { } anim)
			src = anim.GetFrame(tex);
		else
			src = tex.Frame();

		// Fit to 32x32, preserve aspect.
		float scale = (float)CellPx / Math.Max(src.Width, src.Height);
		Vector2 center = topLeft + new Vector2(CellPx * 0.5f, CellPx * 0.5f);
		Vector2 origin = src.Size() * 0.5f;

		sb.Draw(tex, center, src, tint, 0f, origin, scale, SpriteEffects.None, 0f);
	}
}
