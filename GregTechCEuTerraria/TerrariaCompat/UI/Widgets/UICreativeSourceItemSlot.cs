#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Minimal phantom slot for the Creative Chest source-type setter. Click with
// a held item -> setter fires with a count-1 clone (held stack unconsumed).
// Click with empty cursor -> setter fires with null/Air to clear. Right-click
// also clears, matching vanilla phantom-slot UX. Display via vanilla
// `ItemSlot.Draw` like every other slot widget.
public sealed class UICreativeSourceItemSlot : UIElement
{
	private const int Native = 22;
	private const float VanillaNativeSlotPixels = 52f;

	private readonly Func<Item> _getter;
	private readonly Action<Item?> _setter;
	private readonly Item[] _render = { new() };

	public UICreativeSourceItemSlot(Func<Item> getter, Action<Item?> setter)
	{
		_getter = getter;
		_setter = setter;
		Width  = StyleDimension.FromPixels(Native);
		Height = StyleDimension.FromPixels(Native);
	}

	public override void LeftMouseDown(UIMouseEvent evt)
	{
		base.LeftMouseDown(evt);
		var cursor = Main.mouseItem;
		if (cursor is null || cursor.IsAir) _setter(null);
		else
		{
			var clone = cursor.Clone();
			clone.stack = 1;
			_setter(clone);
		}
		SoundEngine.PlaySound(SoundID.MenuTick);
	}

	public override void RightMouseDown(UIMouseEvent evt)
	{
		base.RightMouseDown(evt);
		_setter(null);
		SoundEngine.PlaySound(SoundID.MenuTick);
	}

	protected override void DrawSelf(SpriteBatch sb)
	{
		_render[0] = _getter() ?? new Item();
		var bounds = GetDimensions().ToRectangle();
		float oldScale = Main.inventoryScale;
		Main.inventoryScale = bounds.Width / VanillaNativeSlotPixels;
		try
		{
			if (IsMouseHovering)
			{
				Main.LocalPlayer.mouseInterface = true;
				ItemSlot.MouseHover(_render, ItemSlot.Context.ChestItem, 0);
			}
			ItemSlot.Draw(sb, _render, ItemSlot.Context.ChestItem, 0, new Vector2(bounds.X, bounds.Y));
		}
		finally
		{
			Main.inventoryScale = oldScale;
		}
	}
}
