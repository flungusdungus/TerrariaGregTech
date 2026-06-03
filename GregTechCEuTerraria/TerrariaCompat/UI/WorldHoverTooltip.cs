#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Shared "cursor is over a world thing" surface (placed wire/pipe/...) - owns the
// hover tooltip text and the world-cell highlight box
public sealed class WorldHoverTooltip : ModSystem
{
	private static string? _pending;
	private static (int x, int y, int wTiles, int hTiles, Color color)? _pendingHighlight;

	public static void Set(string text) => _pending = text;

	// Tile-space cell-outline highlight, suppressed under UI like the tooltip.
	public static void SetHighlight(int tileX, int tileY, Color color, int wTiles = 1, int hTiles = 1)
		=> _pendingHighlight = (tileX, tileY, wTiles, hTiles, color);

	public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
	{
		// Game-scale highlight (world-aligned), before "Vanilla: Mouse Text" (late = mouseInterface set).
		int mouseTextIdx = layers.FindIndex(l => l.Name == "Vanilla: Mouse Text");
		int insertAt = mouseTextIdx >= 0 ? mouseTextIdx : layers.Count;
		layers.Insert(insertAt, new LegacyGameInterfaceLayer(
			"GregTechCEuTerraria: World Hover Highlight",
			FlushHighlight,
			InterfaceScaleType.Game));

		// UI-scale tooltip text, drawn on top of the highlight.
		UILayers.InsertModal(layers,
			"GregTechCEuTerraria: World Hover Tooltip",
			() =>
			{
				if (_pending is not null && !Main.LocalPlayer.mouseInterface)
				{
					Main.LocalPlayer.cursorItemIconEnabled = false;
					Main.instance.MouseText(_pending);
				}
				_pending = null;
				return true;
			});
	}

	private static bool FlushHighlight()
	{
		if (_pendingHighlight is { } hl
			&& !Main.dedServ && !Main.gameMenu
			&& Main.LocalPlayer is not null && !Main.LocalPlayer.mouseInterface)
		{
			var sb = Main.spriteBatch;
			var pixel = Terraria.GameContent.TextureAssets.MagicPixel.Value;
			var sp = Main.screenPosition;
			int rx = (int)System.Math.Round(hl.x * 16f - sp.X);
			int ry = (int)System.Math.Round(hl.y * 16f - sp.Y);
			int w = hl.wTiles * 16, h = hl.hTiles * 16;
			sb.Draw(pixel, new Rectangle(rx, ry, w, 1), hl.color);
			sb.Draw(pixel, new Rectangle(rx, ry + h - 1, w, 1), hl.color);
			sb.Draw(pixel, new Rectangle(rx, ry, 1, h), hl.color);
			sb.Draw(pixel, new Rectangle(rx + w - 1, ry, 1, h), hl.color);
		}
		_pendingHighlight = null;
		return true;
	}
}
