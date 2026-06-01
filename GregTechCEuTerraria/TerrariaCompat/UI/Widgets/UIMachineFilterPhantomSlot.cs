#nullable enable
using System;
using GregTechCEuTerraria.Api.Cover.Filter;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Net.Actions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// One phantom matcher slot of a machine-owned SimpleItemFilter - the server-
// authoritative analogue of UIMagnetPhantomSlot. Same verbatim
// PhantomSlotWidget click semantics; differs only in dispatch - the magnet
// edits client-side (private item), this routes through MachineFilterAction
// (a server-authoritative machine state mutation).
public sealed class UIMachineFilterPhantomSlot : UIElement
{
	public const int NativeUnscaledSize = 22;
	private const float VanillaNativeSlotPixels = 52f;

	private readonly MetaMachine _entity;
	private readonly Func<SimpleItemFilter?> _filter;
	private readonly int _index;
	private readonly Item[] _render = { new() };

	private bool _leftDown, _rightDown, _midDown;

	public UIMachineFilterPhantomSlot(MetaMachine entity, Func<SimpleItemFilter?> filter, int index)
	{
		_entity = entity;
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
				HandleClicks();
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

	private void HandleClicks()
	{
		bool shift = Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift);
		bool leftPress  = Main.mouseLeft   && !_leftDown;
		bool rightPress = Main.mouseRight  && !_rightDown;
		bool midPress   = Main.mouseMiddle && !_midDown;
		_leftDown  = Main.mouseLeft;
		_rightDown = Main.mouseRight;
		_midDown   = Main.mouseMiddle;

		int button = leftPress ? 0 : rightPress ? 1 : midPress ? 2 : -1;
		if (button < 0) return;

		MachineActions.Send(MachineFilterAction.Matcher(_index, button, shift, Main.mouseItem), _entity);
		SoundEngine.PlaySound(SoundID.MenuTick);
	}
}
