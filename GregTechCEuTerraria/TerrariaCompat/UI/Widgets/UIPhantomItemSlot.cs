#nullable enable
using GregTechCEuTerraria.Api.Cover;
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

// One phantom matcher slot of a cover's SimpleItemFilter. Verbatim
// PhantomSlotWidget click semantics (LMB=held count, RMB=1, empty +LMB/RMB
// = +/-1, Shift halves/doubles, MMB clears). Held item is never consumed.
// Server-authoritative via CoverFilterAction; live cover resolved per frame.
public sealed class UIPhantomItemSlot : UIElement
{
	public const int NativeUnscaledSize = 22;
	private const float VanillaNativeSlotPixels = 52f;

	private readonly ICoverable _entity;
	private readonly CoverSide _side;
	private readonly int _index;
	private readonly Item[] _render = { new() };

	private bool _leftDown, _rightDown, _midDown;

	public UIPhantomItemSlot(ICoverable entity, CoverSide side, int index)
	{
		_entity = entity;
		_side = side;
		_index = index;
		Width = StyleDimension.FromPixels(NativeUnscaledSize);
		Height = StyleDimension.FromPixels(NativeUnscaledSize);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var filter = _entity.GetCoverAtSide(_side)?.UiItemFilter;
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
				ShowTooltip();
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

	// Hand-rolled text via MouseText (instead of ItemSlot.MouseHover) so the
	// click cheat-sheet rides alongside the item name.
	private void ShowTooltip()
	{
		var slot = _render[0];
		var sb = new System.Text.StringBuilder();
		if (slot.IsAir)
		{
			sb.Append("Empty matcher slot");
		}
		else
		{
			sb.Append(slot.Name);
			sb.Append("  *  amount: ");
			sb.Append(slot.stack);
		}
		sb.Append('\n');
		if (slot.IsAir)
		{
			sb.Append("[c/AAAAAA:LMB / RMB with held item:] set type\n");
			sb.Append("[c/AAAAAA:LMB] = amount of held stack   [c/AAAAAA:RMB] = 1");
		}
		else
		{
			sb.Append("[c/AAAAAA:Empty hand  LMB] -1   [c/AAAAAA:RMB] +1\n");
			sb.Append("[c/AAAAAA:Shift+LMB] halve   [c/AAAAAA:Shift+RMB] double\n");
			sb.Append("[c/AAAAAA:Middle-click] clear   [c/AAAAAA:LMB/RMB with held] replace");
		}
		Main.instance.MouseText(sb.ToString());
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

		// Button ordinal matches slotClickPhantom: 0=L, 1=R, 2=M.
		int button = leftPress ? 0 : rightPress ? 1 : midPress ? 2 : -1;
		if (button < 0) return;

		CoverActions.Send(
			CoverFilterAction.Matcher(_side, fluid: false, _index, button, shift, Main.mouseItem), _entity);
		SoundEngine.PlaySound(SoundID.MenuTick);
	}
}
