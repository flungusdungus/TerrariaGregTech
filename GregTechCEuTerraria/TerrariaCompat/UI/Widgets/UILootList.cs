#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Loot;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Virtualized loot rows mirroring UIRecipeList. Row shape:
//   [source-icon] [source-label] -> [target-icon] [target-name] (detail)
// Two hit zones (source / target icons) each route through BrowserSlotInteraction.
public sealed class UILootList : UIElement
{
	private readonly Func<IReadOnlyList<LootRegistry.LootEntry>> _sourceProvider;
	private readonly string _emptyHint;
	private int _scrollOffsetPx;
	private bool _leftDown;
	private bool _rightDown;

	private bool _dragging;
	private int  _dragAnchorOffsetPx;

	private const int ScrollbarWidth = 18;
	private const int Margin = 6;
	private const int MinThumbHeight = 28;
	private const int RowHeight = 30;
	private const int IconSize = 24;
	private const float VanillaNativeSlotPixels = 52f;

	private static readonly Item[] _scratch = { new() };

	public UILootList(Func<IReadOnlyList<LootRegistry.LootEntry>> sourceProvider,
		string emptyHint = "No matches")
	{
		_sourceProvider = sourceProvider;
		_emptyHint = emptyHint;
	}

	public override void ScrollWheel(UIScrollWheelEvent evt)
	{
		base.ScrollWheel(evt);
		_scrollOffsetPx -= evt.ScrollWheelValue / 6;
		_scrollOffsetPx = Math.Max(0, _scrollOffsetPx);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var outer = GetDimensions().ToRectangle();
		var px = TextureAssets.MagicPixel.Value;

		spriteBatch.Draw(px, outer, new Color(20, 22, 50) * 0.45f);

		if (IsMouseHovering)
		{
			Main.LocalPlayer.mouseInterface = true;
			PlayerInput.LockVanillaMouseScroll("GregTechCEuTerraria/LootBrowser");
		}

		var content = new Rectangle(
			outer.X + Margin,
			outer.Y + Margin,
			outer.Width - Margin * 2 - ScrollbarWidth,
			outer.Height - Margin * 2);

		var src = _sourceProvider();
		if (src.Count == 0)
		{
			Terraria.Utils.DrawBorderString(spriteBatch, _emptyHint,
				new Vector2(content.X + 8, content.Y + 8),
				Color.LightGray, 0.85f);
			return;
		}

		int rowH   = RowHeight;
		int totalH = src.Count * rowH;
		int viewH  = content.Height;
		int maxOffset = Math.Max(0, totalH - viewH);
		if (_scrollOffsetPx > maxOffset) _scrollOffsetPx = maxOffset;

		int firstRow = (_scrollOffsetPx + rowH - 1) / rowH;
		int lastRow  = Math.Min(src.Count - 1, (_scrollOffsetPx + viewH) / rowH - 1);

		var mouse = new Point((int)Main.MouseScreen.X, (int)Main.MouseScreen.Y);

		// Scrollbar interaction first (UIRecipeList pattern).
		bool draggingThisFrame = false;
		Rectangle trackRect = Rectangle.Empty;
		Rectangle thumbRect = Rectangle.Empty;
		if (totalH > viewH)
		{
			int barX = outer.Right - Margin - ScrollbarWidth;
			trackRect = new Rectangle(barX, content.Y, ScrollbarWidth, content.Height);

			float frac = (float)viewH / totalH;
			int thumbH = Math.Max(MinThumbHeight, (int)(content.Height * frac));
			int travel = content.Height - thumbH;
			int thumbY = content.Y + (travel > 0 ? (int)(travel * ((float)_scrollOffsetPx / maxOffset)) : 0);
			thumbRect = new Rectangle(barX, thumbY, ScrollbarWidth, thumbH);

			if (Main.mouseLeft && !_leftDown && trackRect.Contains(mouse))
			{
				_dragging = true;
				_dragAnchorOffsetPx = thumbRect.Contains(mouse) ? (mouse.Y - thumbY) : (thumbH / 2);
			}

			if (_dragging && Main.mouseLeft)
			{
				int newThumbTop = mouse.Y - _dragAnchorOffsetPx;
				int travelMax   = Math.Max(1, content.Height - thumbH);
				int clampedTop  = Math.Clamp(newThumbTop - content.Y, 0, travelMax);
				_scrollOffsetPx = (int)((float)clampedTop / travelMax * maxOffset);
				draggingThisFrame = true;
			}
			else if (!Main.mouseLeft)
			{
				_dragging = false;
			}

			if (draggingThisFrame)
			{
				thumbY = content.Y + (travel > 0 ? (int)(travel * ((float)_scrollOffsetPx / maxOffset)) : 0);
				thumbRect = new Rectangle(barX, thumbY, ScrollbarWidth, thumbH);
				firstRow = (_scrollOffsetPx + rowH - 1) / rowH;
				lastRow  = Math.Min(src.Count - 1, (_scrollOffsetPx + viewH) / rowH - 1);
			}
		}

		int hoveredRow = -1;
		for (int i = firstRow; i <= lastRow; i++)
		{
			int yTop = content.Y - _scrollOffsetPx + i * rowH;
			var rowBounds = new Rectangle(content.X, yTop, content.Width, rowH);

			bool rowHovered = !draggingThisFrame && rowBounds.Contains(mouse) && content.Contains(mouse);
			if (rowHovered)
			{
				spriteBatch.Draw(px, rowBounds, new Color(100, 130, 200) * 0.25f);
				hoveredRow = i;
			}

			DrawRow(spriteBatch, rowBounds, src[i]);
		}

		if (hoveredRow >= 0)
		{
			Main.LocalPlayer.mouseInterface = true;
			var entry = src[hoveredRow];
			var rowBounds = new Rectangle(content.X, content.Y - _scrollOffsetPx + hoveredRow * rowH, content.Width, rowH);
			ComputeIconRects(rowBounds, entry, out var sourceRect, out var targetRect);
			bool overSource = sourceRect.Width > 0 && sourceRect.Contains(mouse);
			bool overTarget = targetRect.Width > 0 && targetRect.Contains(mouse);

			// Loot rows are "about" the target - push to BrowserHover regardless
			// of which zone the cursor is in, so R/U scopes to the target.
			if (entry.TargetItem > 0) BrowserHover.SetItem(entry.TargetItem);

			// Source zone tooltips the source NPC/item; target zone the target item.
			if (overSource && entry.SourceNpcType > 0)
			{
				Main.instance.MouseText(entry.SourceLabel);
			}
			else if (overSource && entry.SourceIconItem > 0)
			{
				Main.HoverItem = new Item();
				Main.HoverItem.SetDefaults(entry.SourceIconItem);
				Main.instance.MouseText("");
			}
			else if (entry.TargetItem > 0)
			{
				Main.HoverItem = new Item();
				Main.HoverItem.SetDefaults(entry.TargetItem);
				LootTooltipGlobal.PushHover(entry.SourceLabel, entry.Detail);
				Main.instance.MouseText("");
			}
			else
			{
				Main.instance.MouseText(entry.SourceLabel);
			}

			if (!_dragging)
			{
				var click = BrowserSlotInteraction.Poll(_leftDown, _rightDown);
				if (overSource)
				{
					if (entry.SourceNpcType > 0)
						BrowserSlotInteraction.HandleNpc(click, entry.SourceNpcType);
					else if (entry.SourceIconItem > 0)
						BrowserSlotInteraction.HandleItem(click, entry.SourceIconItem,
							inFavoritesPane: false);
				}
				else if (overTarget && entry.TargetItem > 0)
				{
					BrowserSlotInteraction.HandleItem(click, entry.TargetItem,
						inFavoritesPane: false);
				}
				else if (entry.TargetItem > 0)
				{
					// Middle-of-row click -> pivot to target.
					if (click.Lmb || click.Rmb)
						BrowserSlotInteraction.HandleItem(click, entry.TargetItem,
							inFavoritesPane: false);
				}
			}
		}
		_leftDown  = Main.mouseLeft;
		_rightDown = Main.mouseRight;

		if (totalH > viewH)
		{
			spriteBatch.Draw(px, trackRect, new Color(10, 12, 30) * 0.7f);
			spriteBatch.Draw(px, new Rectangle(trackRect.X, trackRect.Y, 1, trackRect.Height), new Color(60, 70, 100));
			spriteBatch.Draw(px, new Rectangle(trackRect.Right - 1, trackRect.Y, 1, trackRect.Height), new Color(60, 70, 100));

			bool thumbHot = _dragging || thumbRect.Contains(mouse);
			var thumbColor = thumbHot ? new Color(180, 200, 240) : new Color(140, 160, 220);
			spriteBatch.Draw(px, thumbRect, thumbColor);

			spriteBatch.Draw(px, new Rectangle(thumbRect.X, thumbRect.Y, thumbRect.Width, 1), Color.White * 0.5f);
			spriteBatch.Draw(px, new Rectangle(thumbRect.X, thumbRect.Y, 1, thumbRect.Height), Color.White * 0.5f);

			if (thumbHot) Main.LocalPlayer.mouseInterface = true;
		}
	}

	private static void DrawRow(SpriteBatch sb, Rectangle bounds, in LootRegistry.LootEntry entry)
	{
		int y = bounds.Y + (bounds.Height - IconSize) / 2;
		int x = bounds.X + 4;

		// Town-NPC head for shops (no banner item); item icon otherwise.
		if (entry.SourceHeadIndex > 0
		    && entry.SourceHeadIndex < TextureAssets.NpcHead.Length)
		{
			DrawNpcHeadFit(sb, new Rectangle(x, y, IconSize, IconSize), entry.SourceHeadIndex);
			x += IconSize + 4;
		}
		else if (entry.SourceIconItem > 0)
		{
			DrawItemIconFit(sb, new Rectangle(x, y, IconSize, IconSize), entry.SourceIconItem);
			x += IconSize + 4;
		}

		Vector2 labelPos = new(x, bounds.Y + (bounds.Height - 14) / 2 + 2);
		float labelScale = 0.78f;
		var labelColor = entry.Kind switch
		{
			LootRegistry.LootKind.Shop    => new Color(255, 220, 140),
			LootRegistry.LootKind.Shimmer => new Color(220, 180, 255),
			_                             => new Color(230, 230, 230),
		};
		Terraria.Utils.DrawBorderString(sb, entry.SourceLabel, labelPos, labelColor, labelScale);
		float srcW = MeasureWidth(entry.SourceLabel, labelScale);
		x = (int)(labelPos.X + srcW + 6);

		Terraria.Utils.DrawBorderString(sb, "->", new Vector2(x, labelPos.Y), Color.LightGray, labelScale);
		x += 14;

		DrawItemIconFit(sb, new Rectangle(x, y, IconSize, IconSize), entry.TargetItem);
		x += IconSize + 4;

		string targetName = TargetName(entry.TargetItem);
		Terraria.Utils.DrawBorderString(sb, targetName, new Vector2(x, labelPos.Y), Color.White, labelScale);
		float tnameW = MeasureWidth(targetName, labelScale);
		x = (int)(x + tnameW + 8);

		// Detail (chance/price/conditions) - dim, right-trimmed.
		if (!string.IsNullOrEmpty(entry.Detail) && x < bounds.Right - 4)
		{
			Terraria.Utils.DrawBorderString(sb, entry.Detail, new Vector2(x, labelPos.Y),
				new Color(170, 180, 200), 0.72f);
		}
	}

	private static string TargetName(int itemId)
	{
		if (itemId <= 0) return "?";
		return ContentSamples.ItemsByType.TryGetValue(itemId, out var sample)
			? sample.Name ?? "?"
			: "?";
	}

	// Mirrors DrawRow's x walk so hit zones match the on-screen positions.
	// Width==0 for an icon that isn't drawn.
	private static void ComputeIconRects(Rectangle bounds, in LootRegistry.LootEntry entry,
		out Rectangle sourceRect, out Rectangle targetRect)
	{
		int y = bounds.Y + (bounds.Height - IconSize) / 2;
		int x = bounds.X + 4;
		bool hasSource = entry.SourceHeadIndex > 0 || entry.SourceIconItem > 0;
		sourceRect = hasSource ? new Rectangle(x, y, IconSize, IconSize) : Rectangle.Empty;
		if (hasSource) x += IconSize + 4;

		const float labelScale = 0.78f;
		float srcLabelW = MeasureWidth(entry.SourceLabel, labelScale);
		x = (int)(x + srcLabelW + 6 + 14);   // +6 gap to arrow, +14 arrow width
		targetRect = entry.TargetItem > 0
			? new Rectangle(x, y, IconSize, IconSize)
			: Rectangle.Empty;
	}

	private static float MeasureWidth(string text, float scale)
	{
		if (string.IsNullOrEmpty(text)) return 0f;
		return FontAssets.MouseText.Value.MeasureString(text).X * scale;
	}

	private static void DrawNpcHeadFit(SpriteBatch sb, Rectangle dest, int headIndex)
	{
		var asset = TextureAssets.NpcHead[headIndex];
		if (asset is null || !asset.IsLoaded) return;
		var tex = asset.Value;
		// Aspect-fit centred (NPC heads are typically 30x30 / 38x40).
		float scale = System.Math.Min(
			(float)dest.Width / tex.Width,
			(float)dest.Height / tex.Height);
		int drawW = (int)(tex.Width * scale);
		int drawH = (int)(tex.Height * scale);
		int dx = dest.X + (dest.Width - drawW) / 2;
		int dy = dest.Y + (dest.Height - drawH) / 2;
		sb.Draw(tex, new Rectangle(dx, dy, drawW, drawH), Color.White);
	}

	private static void DrawItemIconFit(SpriteBatch sb, Rectangle dest, int itemType)
	{
		if (itemType <= 0 || itemType >= TextureAssets.Item.Length) return;
		_scratch[0].SetDefaults(itemType);
		float oldScale = Main.inventoryScale;
		Main.inventoryScale = dest.Width / VanillaNativeSlotPixels;
		try
		{
			ItemSlot.Draw(sb, _scratch, ItemSlot.Context.CraftingMaterial, 0,
				new Vector2(dest.X, dest.Y));
		}
		finally
		{
			Main.inventoryScale = oldScale;
		}
	}
}
