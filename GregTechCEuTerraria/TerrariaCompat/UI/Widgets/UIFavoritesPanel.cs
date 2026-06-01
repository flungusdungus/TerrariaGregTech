#nullable enable
using GregTechCEuTerraria.Api.Fluids;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Pinned-items pane right of the recipe browser. Alt+click in the browser
// pins; click semantics route through BrowserSlotInteraction with
// inFavoritesPane=true so Alt+LMB REMOVES. Mouse-wheel scroll (no scrollbar).
public sealed class UIFavoritesPanel : UITerrariaPanel
{
	private const int Cols = 4;
	private const int CellSize = 30;
	private const int CellPad = 2;
	private const int Margin = 6;
	public const int PanelWidth = Cols * (CellSize + CellPad) + Margin * 2;
	private const int ScrollbarWidth = 0;
	private const float VanillaNativeSlotPixels = 52f;

	private static readonly Item[] _slotItems = { new() };

	private int _scroll;
	private bool _leftDown;
	private bool _rightDown;

	public UIFavoritesPanel()
	{
		Width  = StyleDimension.FromPixels(PanelWidth);
	}

	public void SetHeight(float h) => Height = StyleDimension.FromPixels(h);

	public override void ScrollWheel(UIScrollWheelEvent evt)
	{
		base.ScrollWheel(evt);
		if (!IsMouseHovering) return;
		_scroll -= evt.ScrollWheelValue / 6;
		if (_scroll < 0) _scroll = 0;
	}

	protected override void DrawSelf(SpriteBatch sb)
	{
		base.DrawSelf(sb);

		var outer = GetDimensions().ToRectangle();

		Terraria.Utils.DrawBorderString(sb, "Favorites",
			new Vector2(outer.X + 6, outer.Y + 4), new Color(220, 230, 255), 0.78f);

		var content = new Rectangle(
			outer.X + Margin,
			outer.Y + 22,
			outer.Width - Margin * 2 - ScrollbarWidth,
			outer.Height - 22 - Margin);

		var entries = FavoritesRegistry.Entries;
		if (entries.Count == 0)
		{
			Terraria.Utils.DrawBorderString(sb, "Alt+click\nto pin",
				new Vector2(content.X + 4, content.Y + 8),
				new Color(140, 150, 180), 0.7f);
			_leftDown  = Main.mouseLeft;
			_rightDown = Main.mouseRight;
			return;
		}

		int totalRows = (entries.Count + Cols - 1) / Cols;
		int rowH = CellSize + CellPad;
		int viewH = content.Height;
		int maxScroll = System.Math.Max(0, totalRows * rowH - viewH);
		if (_scroll > maxScroll) _scroll = maxScroll;

		var mouse = new Point((int)Main.MouseScreen.X, (int)Main.MouseScreen.Y);
		bool inside = content.Contains(mouse);
		if (IsMouseHovering)
		{
			Main.LocalPlayer.mouseInterface = true;
			PlayerInput.LockVanillaMouseScroll("GregTechCEuTerraria/FavoritesPanel");
		}

		// Items via UISlot's ItemSlot.Draw pipeline; fluids via the recipe-row
		// helper - same look as in recipe rows.
		float oldScale = Main.inventoryScale;
		Main.inventoryScale = CellSize / VanillaNativeSlotPixels;
		FavoritesRegistry.Entry hovered = default;
		bool hasHovered = false;
		try
		{
			for (int i = 0; i < entries.Count; i++)
			{
				int col = i % Cols;
				int row = i / Cols;
				int yTop = content.Y - _scroll + row * rowH;
				if (yTop + CellSize < content.Y || yTop > content.Bottom) continue;

				var rect = new Rectangle(
					content.X + col * (CellSize + CellPad),
					yTop,
					CellSize, CellSize);

				var entry = entries[i];
				bool isHover = inside && rect.Contains(mouse);

				if (entry.ItemType > 0)
				{
					_slotItems[0].SetDefaults(entry.ItemType);
					if (isHover)
					{
						Main.LocalPlayer.mouseInterface = true;
						ItemSlot.OverrideHover(_slotItems, ItemSlot.Context.CraftingMaterial, 0);
						ItemSlot.MouseHover(_slotItems, ItemSlot.Context.CraftingMaterial, 0);
					}
					ItemSlot.Draw(sb, _slotItems, ItemSlot.Context.CraftingMaterial, 0,
						new Vector2(rect.X, rect.Y));
				}
				else if (entry.FluidId is not null)
				{
					var fluid = FluidRegistry.Get(entry.FluidId);
					BrowserFluidSlot.Draw(sb, rect, fluid,
						amountMb: 0, fallbackLabel: entry.FluidLabel);
					if (isHover)
					{
						Main.LocalPlayer.mouseInterface = true;
						BrowserFluidSlot.EmitTooltip(fluid, amountMb: 0,
							fallbackLabel: entry.FluidLabel);
					}
				}

				if (isHover)
				{
					hovered = entry;
					hasHovered = true;
					if (entry.ItemType > 0)
						BrowserHover.SetItem(entry.ItemType);
					else if (entry.FluidId is not null)
						BrowserHover.SetFluid(entry.FluidId, entry.FluidLabel ?? entry.FluidId);
				}
			}
		}
		finally
		{
			Main.inventoryScale = oldScale;
		}

		// inFavoritesPane=true flips Alt+LMB from add to remove.
		if (hasHovered && !_dragging())
		{
			var click = BrowserSlotInteraction.Poll(_leftDown, _rightDown);
			if (hovered.ItemType > 0)
				BrowserSlotInteraction.HandleItem(click, hovered.ItemType,
					inFavoritesPane: true);
			else if (hovered.FluidId is not null)
			{
				var fluid = FluidRegistry.Get(hovered.FluidId);
				if (fluid is not null)
					BrowserSlotInteraction.HandleFluid(click, fluid,
						recipeAmountMb: null, inFavoritesPane: true);
				else if (click.Alt && click.Lmb)
					FavoritesRegistry.RemoveFluid(hovered.FluidId);
			}
		}
		_leftDown  = Main.mouseLeft;
		_rightDown = Main.mouseRight;
	}

	// No scrollbar yet - stub for parity with UIRecipeList.
	private bool _dragging() => false;

}
