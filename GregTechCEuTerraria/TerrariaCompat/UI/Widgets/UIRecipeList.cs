#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Virtualized recipe list - no per-row UIElements; only visible rows draw.
public sealed class UIRecipeList : UIElement
{
	private readonly Func<IReadOnlyList<GTRecipe>> _sourceProvider;
	private readonly string _emptyHint;

	public Action<GTRecipe>? OnSelectRecipe;

	// Swallow the left-press that's still held when selection mode begins
	private bool _awaitRelease;
	public void IgnoreHeldClick() => _awaitRelease = true;

	private int _scrollOffsetPx;
	// Edge-tracked mouse - one action per press, not per held frame.
	private bool _leftDown;
	private bool _rightDown;

	// Scrollbar drag - _dragAnchorOffsetPx is the cursor's Y offset INSIDE the
	// thumb at grab time so dragging tracks 1:1 without snap.
	private bool _dragging;
	private int  _dragAnchorOffsetPx;

	private const int ScrollbarWidth = 18;
	private const int Margin = 6;
	private const int MinThumbHeight = 28;
	private const int SelectGutter = 44; // left gutter for the "+" pick button in selection mode
	private const int SelectBtnSize = 36;

	private static void DrawPlusButton(SpriteBatch sb, Rectangle rect, bool hot)
	{
		var px = TextureAssets.MagicPixel.Value;
		float t = (float)(0.5 + 0.5 * Math.Sin(Main.GameUpdateCount * 0.12));
		var bg = Color.Lerp(new Color(36, 130, 60), new Color(96, 232, 122), t);
		if (hot) bg = Color.Lerp(bg, Color.White, 0.25f);
		sb.Draw(px, rect, bg);
		var border = hot ? new Color(200, 255, 215) : new Color(28, 90, 45);
		sb.Draw(px, new Rectangle(rect.X, rect.Y, rect.Width, 1), border);
		sb.Draw(px, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), border);
		sb.Draw(px, new Rectangle(rect.X, rect.Y, 1, rect.Height), border);
		sb.Draw(px, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), border);
		var font = FontAssets.MouseText.Value;
		const float scale = 1.9f;
		var size = font.MeasureString("+") * scale;
		Terraria.Utils.DrawBorderString(sb, "+",
			new Vector2(rect.X + (rect.Width - size.X) / 2f, rect.Y + (rect.Height - size.Y) / 2f - 2),
			Color.White, scale);
	}

	public UIRecipeList(Func<IReadOnlyList<GTRecipe>> sourceProvider, string emptyHint = "No recipes")
	{
		_sourceProvider = sourceProvider;
		_emptyHint = emptyHint;
	}

	// Resolves a cell's Content into item / fluid / tag - peels Sized /
	// IntProvider wrappers to reach the concrete typed Ingredient.
	private static void ResolveCell(Api.Recipe.Content.Content content,
		out int itemType, out int itemAmount,
		out FluidType? fluid, out int fluidAmountMb,
		out string? tagLabel, out HashSet<int>? tagMembers)
	{
		itemType = 0; itemAmount = 1; fluid = null; fluidAmountMb = 0;
		tagLabel = null; tagMembers = null;

		var raw = (Ingredient)content.Payload;
		itemAmount = raw switch
		{
			SizedIngredient s         => s.Amount,
			IntProviderIngredient ipi => ipi.RollSampledCount(),
			_                         => 1,
		};
		if (itemAmount <= 0) itemAmount = 1;
		var inner = Inner(raw);
		// Single-member tags collapse to a normal item click.
		if (inner is TagIngredient tag && tag.GetItems().Count > 0)
		{
			var members = new HashSet<int>();
			foreach (var m in tag.GetItems())
				if (m.type > Terraria.ID.ItemID.None) members.Add(m.type);
			if (members.Count >= 2)
			{
				tagLabel = StripNs(tag.TagName);
				tagMembers = members;
				return;
			}
			itemType = tag.GetItems()[0].type;
			return;
		}

		switch (inner)
		{
			case FluidIngredient fi:
				fluid = fi.ExactType
				     ?? (fi.GetFluids().Count > 0 ? fi.GetFluids()[0] : null);
				fluidAmountMb = fi.Amount;
				return;
			case ItemStackIngredient isi when isi.ItemType > 0:    itemType = isi.ItemType; return;
			case NBTPredicateIngredient nbt when nbt.ItemType > 0: itemType = nbt.ItemType; return;
			case FluidContainerIngredient fc:
				fluid = fc.Fluid.ExactType
				     ?? (fc.Fluid.GetFluids().Count > 0 ? fc.Fluid.GetFluids()[0] : null);
				fluidAmountMb = fc.Fluid.Amount;
				return;
		}

		// IntProviderFluidIngredient: Inner returned the unwrapped FluidIngredient;
		// pick up the rolled amount from the original wrapper here.
		if (Inner((Ingredient)content.Payload) is IntProviderFluidIngredient ipfi)
			fluidAmountMb = ipfi.RollSampledCount();
	}

	private static string StripNs(string id)
	{
		int colon = id.IndexOf(':');
		return colon >= 0 ? id.Substring(colon + 1) : id;
	}

	// Records the hovered cell for R/U hotkeys (Main.HoverItem doesn't carry fluids).
	private static void RecordHover(Api.Recipe.Content.Content content)
	{
		ResolveCell(content, out int itemType, out _, out var fluid, out _,
			out string? tagLabel, out var tagMembers);
		if (tagLabel is not null && tagMembers is not null)
			BrowserHover.SetTag(tagLabel, tagMembers);
		else if (itemType > 0) BrowserHover.SetItem(itemType);
		else if (fluid is not null) BrowserHover.SetFluid(fluid.Id, fluid.DisplayName);
	}

	private static Ingredient Inner(Ingredient ing) => ing switch
	{
		SizedIngredient sized      => Inner(sized.Inner),
		IntProviderIngredient ipi  => Inner(ipi.Inner),
		IntProviderFluidIngredient ipfi => ipfi.Inner,
		_                          => ing,
	};

	public override void ScrollWheel(UIScrollWheelEvent evt)
	{
		base.ScrollWheel(evt);
		// One notch ~ 3 rows.
		_scrollOffsetPx -= evt.ScrollWheelValue / 6;
		_scrollOffsetPx = Math.Max(0, _scrollOffsetPx);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var outer = GetDimensions().ToRectangle();
		var px = TextureAssets.MagicPixel.Value;

		if (_awaitRelease && !Main.mouseLeft) _awaitRelease = false;

		spriteBatch.Draw(px, outer, new Color(20, 22, 50) * 0.45f);

		// Suppress vanilla mouse-wheel for the frame; arm the HoverItemTracker
		// guard so passive hover here doesn't push into the filter (clicks do).
		if (IsMouseHovering)
		{
			Main.LocalPlayer.mouseInterface = true;
			PlayerInput.LockVanillaMouseScroll("GregTechCEuTerraria/RecipeBrowser");
			HoverItemTracker.SuppressNextHoverPick();
		}

		// Content rect (right-edge scrollbar gutter reserved).
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
			_leftDown  = Main.mouseLeft;
			_rightDown = Main.mouseRight;
			return;
		}

		int rowH = RecipeRowRenderer.RowHeight;
		int totalH = src.Count * rowH;
		int viewH = content.Height;
		int maxOffset = Math.Max(0, totalH - viewH);
		if (_scrollOffsetPx > maxOffset) _scrollOffsetPx = maxOffset;

		// Only draw fully-visible rows (avoids the mid-Draw scissor dance).
		int firstRow = (_scrollOffsetPx + rowH - 1) / rowH;
		int lastRow = Math.Min(src.Count - 1, (_scrollOffsetPx + viewH) / rowH - 1);

		var mouse = new Point((int)Main.MouseScreen.X, (int)Main.MouseScreen.Y);

		// Scrollbar interaction first - drag updates _scrollOffsetPx before row
		// layout reads it (avoids 1-frame visual lag).
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

			// LMB-down anywhere in the track begins a drag; on the thumb the
			// anchor preserves mouse-Y offset (track-click recenters on cursor).
			if (Main.mouseLeft && !_leftDown && trackRect.Contains(mouse))
			{
				_dragging = true;
				_dragAnchorOffsetPx = thumbRect.Contains(mouse) ? mouse.Y - thumbY : thumbH / 2;
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
		bool picked = false; // a Select-Recipe click exits the row loop AND skips the hover/tooltip block below
		int selGutter = OnSelectRecipe != null ? SelectGutter : 0;
		for (int i = firstRow; i <= lastRow; i++)
		{
			int yTop = content.Y - _scrollOffsetPx + i * rowH;
			var rowBounds = new Rectangle(content.X, yTop, content.Width, rowH);
			var contentBounds = selGutter > 0
				? new Rectangle(rowBounds.X + selGutter, yTop, rowBounds.Width - selGutter, rowH)
				: rowBounds;

			// Skip hover highlight while dragging (else rows flash as cursor crosses).
			bool rowHovered = !draggingThisFrame && rowBounds.Contains(mouse) && content.Contains(mouse);
			if (rowHovered)
			{
				spriteBatch.Draw(px, rowBounds, new Color(100, 130, 200) * 0.25f);
				hoveredRow = i;
			}

			RecipeRowRenderer.Draw(spriteBatch, contentBounds, src[i], Color.White);

			if (selGutter > 0)
			{
				var selBtn = new Rectangle(rowBounds.X + 4, yTop + (rowH - SelectBtnSize) / 2, SelectBtnSize, SelectBtnSize);
				bool overSel = !draggingThisFrame && content.Contains(mouse) && selBtn.Contains(mouse);
				DrawPlusButton(spriteBatch, selBtn, overSel);
				if (overSel)
				{
					Main.LocalPlayer.mouseInterface = true;
					Main.instance.MouseText("Select this recipe");
					if (!_awaitRelease && Main.mouseLeft && !_leftDown && !_dragging)
					{
						OnSelectRecipe!(src[i]);
						picked = true;
						break;
					}
				}
			}
		}

		if (hoveredRow >= 0 && !picked)
		{
			Main.LocalPlayer.mouseInterface = true;
			int hyTop = content.Y - _scrollOffsetPx + hoveredRow * rowH;
			var rowBounds = selGutter > 0
				? new Rectangle(content.X + selGutter, hyTop, content.Width - selGutter, rowH)
				: new Rectangle(content.X, hyTop, content.Width, rowH);

			// Quick-craft chip wins click-priority over any overlapping cell.
			var craftRecipe = RecipeRowRenderer.FindAvailableVanillaCraft(src[hoveredRow]);
			var craftBtn    = craftRecipe != null
				? RecipeRowRenderer.CraftButtonRect(rowBounds)
				: Rectangle.Empty;
			bool overCraft  = craftRecipe != null && craftBtn.Contains(mouse);

			if (overCraft)
			{
				// Vanilla parity per iteration: Shift = x10. Don't skip the
				// per-iter availability re-check - bypassing it dupes the held
				// cursor item through the swap inside Main.CraftItem.
				int qty = ItemSlot.ShiftInUse ? 10 : 1;
				Main.instance.MouseText($"Craft {qty}x {craftRecipe!.createItem.Name}");
				if (Main.mouseLeft && !_leftDown && !_dragging)
				{
					for (int n = 0; n < qty; n++)
					{
						Terraria.Recipe.FindRecipes(canDelayCheck: false);
						bool stillAvailable = false;
						for (int i = 0; i < Main.numAvailableRecipes; i++)
							if (Main.availableRecipe[i] >= 0 &&
							    ReferenceEquals(Main.recipe[Main.availableRecipe[i]], craftRecipe))
							{ stillAvailable = true; break; }
						if (!stillAvailable) break;

						// tML's CraftItem CanStack early-return skips the
						// destination.type==source.type check, so a non-matching
						// cursor item gets duped. Block.
						if (Main.mouseItem.stack > 0
						    && Main.mouseItem.type != craftRecipe.createItem.type)
							break;

						Main.CraftItem(craftRecipe);
					}
				}
			}
			else
			{
				RecipeRowRenderer.EmitTooltipFor(src[hoveredRow], rowBounds, mouse);

				var ing = RecipeRowRenderer.IngredientAt(src[hoveredRow], rowBounds, mouse);
				if (ing is not null) RecordHover(ing);
				if (ing is not null && !_dragging)
				{
					var click = BrowserSlotInteraction.Poll(_leftDown, _rightDown);
					ResolveCell(ing, out int itemType, out int itemAmt,
						out var fluid, out int fluidAmt,
						out string? tagLabel, out var tagMembers);
					if (tagLabel is not null && tagMembers is not null)
						BrowserSlotInteraction.HandleTag(click, tagLabel, tagMembers,
							recipeAmount: itemAmt);
					else if (fluid is not null)
						BrowserSlotInteraction.HandleFluid(click, fluid,
							fluidAmt > 0 ? fluidAmt : (int?)null, inFavoritesPane: false);
					else if (itemType > 0)
						BrowserSlotInteraction.HandleItem(click,
							RecipeRowRenderer.BuildDisplayItem(ing, itemType),
							inFavoritesPane: false,
							recipeAmount: itemAmt);
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

			// Top + left 1-px highlight for the 3D-grabbable affordance.
			spriteBatch.Draw(px, new Rectangle(thumbRect.X, thumbRect.Y, thumbRect.Width, 1), Color.White * 0.5f);
			spriteBatch.Draw(px, new Rectangle(thumbRect.X, thumbRect.Y, 1, thumbRect.Height), Color.White * 0.5f);

			if (thumbHot) Main.LocalPlayer.mouseInterface = true;
		}
	}
}
