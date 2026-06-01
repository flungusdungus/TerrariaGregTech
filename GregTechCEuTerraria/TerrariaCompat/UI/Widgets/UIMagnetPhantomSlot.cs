#nullable enable
using System;
using GregTechCEuTerraria.Api.Cover.Filter;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// One phantom matcher slot of the item magnet's SimpleItemFilter - a "ghost"
// item (type + a configured count), never a real stack. Verbatim
// PhantomSlotWidget click semantics via ItemFilterEdit (LMB sets count = held
// count, RMB sets 1, empty-handed LMB/RMB step -1/+1, Shift halves/doubles,
// middle-click clears).
//
// Unlike the cover phantom slot this edits client-side directly - the magnet is
// a private inventory item, persisted per-stack through the magnet ModItem.
public sealed class UIMagnetPhantomSlot : UIElement
{
	public const int NativeUnscaledSize = 22;
	private const float VanillaNativeSlotPixels = 52f;

	private readonly Func<SimpleItemFilter?> _filter;
	private readonly int _index;
	private readonly Item[] _render = { new() };

	private bool _leftDown, _rightDown, _midDown;

	public UIMagnetPhantomSlot(Func<SimpleItemFilter?> filter, int index)
	{
		_filter = filter;
		_index = index;
		Width = StyleDimension.FromPixels(NativeUnscaledSize);
		Height = StyleDimension.FromPixels(NativeUnscaledSize);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var filter = _filter();
		_render[0] = filter is not null && _index < filter.Matches.Length
			? filter.Matches[_index] : new Item();

		var bounds = GetDimensions().ToRectangle();
		float oldScale = Main.inventoryScale;
		Main.inventoryScale = bounds.Width / VanillaNativeSlotPixels;
		try
		{
			if (IsMouseHovering)
			{
				Main.LocalPlayer.mouseInterface = true;
				HandleClicks(filter);
				ItemSlot.MouseHover(_render, ItemSlot.Context.ChestItem, 0);
			}
			else
			{
				_leftDown = _rightDown = _midDown = false;
			}
			ItemSlot.Draw(spriteBatch, _render, ItemSlot.Context.ChestItem, 0, new Vector2(bounds.X, bounds.Y));
		}
		finally
		{
			Main.inventoryScale = oldScale;
		}
	}

	private void HandleClicks(SimpleItemFilter? filter)
	{
		bool shift = Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift);
		bool leftPress  = Main.mouseLeft   && !_leftDown;
		bool rightPress = Main.mouseRight  && !_rightDown;
		bool midPress   = Main.mouseMiddle && !_midDown;
		_leftDown  = Main.mouseLeft;
		_rightDown = Main.mouseRight;
		_midDown   = Main.mouseMiddle;

		// Button ordinal - 0 left, 1 right, 2 middle - matching slotClickPhantom.
		int button = leftPress ? 0 : rightPress ? 1 : midPress ? 2 : -1;
		if (button < 0 || filter is null) return;

		ItemFilterEdit.MatcherClick(filter, _index, button, shift, Main.mouseItem);
		SoundEngine.PlaySound(SoundID.MenuTick);
	}
}
