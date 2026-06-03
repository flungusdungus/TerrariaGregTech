#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Scrolling slot grid for the browser's Items mode. Source list provided by
// the caller; the grid owns scroll + click dispatch + draw. Clicks route
// through BrowserSlotInteraction; R/U hover hotkeys apply.
public sealed class UIItemGrid : UIElement
{
	private const int CellSize = 36;
	private const int CellPad  = 2;
	private const int Margin   = 6;
	private const int ScrollbarWidth = 14;
	private const int MinThumbHeight = 28;
	private const float VanillaNativeSlotPixels = 52f;

	// SetDefaults clones a fresh ModItem and walks every GlobalItem hook;
	// per-frame per-cell calls visibly stutter at scroll. Cache pays it once.
	private static readonly Dictionary<int, Item> _itemCache = new();
	private static readonly Item[] _drawSlot = { new() };

	private static bool _warmedVanilla;

	public static void WarmVanillaItemTextures()
	{
		if (_warmedVanilla || Main.dedServ) return;
		_warmedVanilla = true;
		for (int t = 1; t < ItemID.Count; t++)
		{
			var asset = TextureAssets.Item[t];
			if (asset != null && (int)asset.State == 0)
				Main.Assets.Request<Texture2D>(asset.Name, AssetRequestMode.AsyncLoad);
		}
	}

	private readonly Func<IReadOnlyList<int>> _source;
	private readonly string _emptyHint;
	private int _scroll;
	private bool _leftDown, _rightDown;
	private bool _dragging;
	private int _dragAnchorOffsetPx;

	public UIItemGrid(Func<IReadOnlyList<int>> source, string emptyHint = "No items match this search")
	{
		_source = source;
		_emptyHint = emptyHint;
	}

	public override void ScrollWheel(UIScrollWheelEvent evt)
	{
		base.ScrollWheel(evt);
		if (!IsMouseHovering) return;
		_scroll -= evt.ScrollWheelValue / 4;
		if (_scroll < 0) _scroll = 0;
	}

	protected override void DrawSelf(SpriteBatch sb)
	{
		var outer = GetDimensions().ToRectangle();
		var px = TextureAssets.MagicPixel.Value;
		sb.Draw(px, outer, new Color(20, 22, 50) * 0.45f);

		if (IsMouseHovering)
		{
			Main.LocalPlayer.mouseInterface = true;
			PlayerInput.LockVanillaMouseScroll("GregTechCEuTerraria/ItemGrid");
			HoverItemTracker.SuppressNextHoverPick();
		}

		var content = new Rectangle(
			outer.X + Margin,
			outer.Y + Margin,
			outer.Width - Margin * 2 - ScrollbarWidth,
			outer.Height - Margin * 2);

		var src = _source();
		if (src.Count == 0)
		{
			Terraria.Utils.DrawBorderString(sb, _emptyHint,
				new Vector2(content.X + 8, content.Y + 8),
				Color.LightGray, 0.85f);
			_leftDown  = Main.mouseLeft;
			_rightDown = Main.mouseRight;
			return;
		}

		int step = CellSize + CellPad;
		int cols = Math.Max(1, (content.Width + CellPad) / step);
		int rows = (src.Count + cols - 1) / cols;
		int totalH = rows * step;
		int viewH = content.Height;
		int maxScroll = Math.Max(0, totalH - viewH);
		if (_scroll > maxScroll) _scroll = maxScroll;

		var mouse = new Point((int)Main.MouseScreen.X, (int)Main.MouseScreen.Y);

		bool draggingThisFrame = false;
		Rectangle trackRect = Rectangle.Empty, thumbRect = Rectangle.Empty;
		if (totalH > viewH)
		{
			int barX = outer.Right - Margin - ScrollbarWidth;
			trackRect = new Rectangle(barX, content.Y, ScrollbarWidth, content.Height);
			float frac = (float)viewH / totalH;
			int thumbH = Math.Max(MinThumbHeight, (int)(content.Height * frac));
			int travel = content.Height - thumbH;
			int thumbY = content.Y + (travel > 0 ? (int)(travel * ((float)_scroll / maxScroll)) : 0);
			thumbRect = new Rectangle(barX, thumbY, ScrollbarWidth, thumbH);

			if (Main.mouseLeft && !_leftDown && trackRect.Contains(mouse))
			{
				_dragging = true;
				_dragAnchorOffsetPx = thumbRect.Contains(mouse)
					? mouse.Y - thumbY
					: thumbH / 2;
			}
			if (_dragging && Main.mouseLeft)
			{
				int newThumbTop = mouse.Y - _dragAnchorOffsetPx;
				int travelMax = Math.Max(1, content.Height - thumbH);
				int clampedTop = Math.Clamp(newThumbTop - content.Y, 0, travelMax);
				_scroll = (int)((float)clampedTop / travelMax * maxScroll);
				draggingThisFrame = true;
			}
			else if (!Main.mouseLeft)
			{
				_dragging = false;
			}
			if (draggingThisFrame)
			{
				thumbY = content.Y + (travel > 0 ? (int)(travel * ((float)_scroll / maxScroll)) : 0);
				thumbRect = new Rectangle(barX, thumbY, ScrollbarWidth, thumbH);
			}
		}

		bool inside = content.Contains(mouse);

		// Fully-inside-content rows only (avoids the mid-Draw scissor dance).
		int firstRow = (_scroll + step - 1) / step;
		int lastRow  = Math.Min(rows - 1, (_scroll + viewH) / step - 1);

		int hoveredType = 0;
		Rectangle hoveredRect = Rectangle.Empty;

		float oldScale = Main.inventoryScale;
		Main.inventoryScale = CellSize / VanillaNativeSlotPixels;
		try
		{
			for (int r = firstRow; r <= lastRow; r++)
			{
				int yTop = content.Y - _scroll + r * step;
				for (int c = 0; c < cols; c++)
				{
					int idx = r * cols + c;
					if (idx >= src.Count) break;
					int xLeft = content.X + c * step;
					var rect = new Rectangle(xLeft, yTop, CellSize, CellSize);
					if (rect.Bottom < content.Y || rect.Y > content.Bottom) continue;

					int itemType = src[idx];
					if (!_itemCache.TryGetValue(itemType, out var cached))
					{
						cached = new Item();
						cached.SetDefaults(itemType);
						_itemCache[itemType] = cached;
					}
					_drawSlot[0] = cached;

					bool isHover = !draggingThisFrame && inside && rect.Contains(mouse);
					if (isHover)
					{
						hoveredType = itemType;
						hoveredRect = rect;
						ItemSlot.OverrideHover(_drawSlot, ItemSlot.Context.CraftingMaterial, 0);
						ItemSlot.MouseHover(_drawSlot, ItemSlot.Context.CraftingMaterial, 0);
					}

					ItemSlot.Draw(sb, _drawSlot, ItemSlot.Context.CraftingMaterial, 0,
						new Vector2(rect.X, rect.Y));
				}
			}
		}
		finally
		{
			Main.inventoryScale = oldScale;
		}

		if (hoveredType > 0)
		{
			Main.LocalPlayer.mouseInterface = true;
			BrowserHover.SetItem(hoveredType);

			var click = BrowserSlotInteraction.Poll(_leftDown, _rightDown);
			BrowserSlotInteraction.HandleItem(click, hoveredType, inFavoritesPane: false);
		}

		_leftDown = Main.mouseLeft;
		_rightDown = Main.mouseRight;

		if (totalH > viewH)
		{
			sb.Draw(px, trackRect, new Color(10, 12, 30) * 0.7f);
			sb.Draw(px, new Rectangle(trackRect.X, trackRect.Y, 1, trackRect.Height), new Color(60, 70, 100));
			sb.Draw(px, new Rectangle(trackRect.Right - 1, trackRect.Y, 1, trackRect.Height), new Color(60, 70, 100));
			bool thumbHot = _dragging || thumbRect.Contains(mouse);
			var thumbColor = thumbHot ? new Color(180, 200, 240) : new Color(140, 160, 220);
			sb.Draw(px, thumbRect, thumbColor);
			sb.Draw(px, new Rectangle(thumbRect.X, thumbRect.Y, thumbRect.Width, 1), Color.White * 0.5f);
			sb.Draw(px, new Rectangle(thumbRect.X, thumbRect.Y, 1, thumbRect.Height), Color.White * 0.5f);
			if (thumbHot) Main.LocalPlayer.mouseInterface = true;
		}
	}

}
