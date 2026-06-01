#nullable enable
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using GregTechCEuTerraria.TerrariaCompat.Net.Actions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Fluid counterpart of UIPhantomItemSlot. Same slotClickPhantom semantics;
// the "held item" is whatever fluid the cursor carries (resolved server-side).
public sealed class UIPhantomFluidSlot : UIElement
{
	public const int NativeUnscaledSize = 22;

	private readonly ICoverable _entity;
	private readonly CoverSide _side;
	private readonly int _index;

	private bool _leftDown, _rightDown, _midDown;

	public UIPhantomFluidSlot(ICoverable entity, CoverSide side, int index)
	{
		_entity = entity;
		_side = side;
		_index = index;
		Width = StyleDimension.FromPixels(NativeUnscaledSize);
		Height = StyleDimension.FromPixels(NativeUnscaledSize);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var filter = _entity.GetCoverAtSide(_side)?.UiFluidFilter;
		FluidStack stack = filter is not null && _index < filter.Matches.Length
			? filter.Matches[_index] : FluidStack.Empty;

		var bounds = GetDimensions().ToRectangle();
		var tex = TextureAssets.MagicPixel.Value;

		spriteBatch.Draw(tex, bounds, new Color(25, 30, 50) * 0.9f);

		if (!stack.IsEmpty)
		{
			var inner = new Rectangle(bounds.X + 2, bounds.Y + 2, bounds.Width - 4, bounds.Height - 4);
			if (!FluidIconRenderer.Draw(spriteBatch, stack.Type!, inner))
				spriteBatch.Draw(tex, inner, FluidIconRenderer.RgbColor(stack.Type!.Color));
		}

		TankFrame.DrawBorder(spriteBatch, bounds, IsMouseHovering
			? Color.Lerp(TankFrame.BorderColor, Color.White, 0.5f)
			: TankFrame.BorderColor);

		// No vanilla overlay for fluid amount - hand-draw.
		if (!stack.IsEmpty)
		{
			string txt = FormatPhantomAmount(stack.Amount);
			var font = FontAssets.ItemStack.Value;
			const float scale = 0.55f;
			var size = font.MeasureString(txt) * scale;
			var pos = new Vector2(
				bounds.Right - size.X - 2,
				bounds.Bottom - size.Y - 1);
			Terraria.Utils.DrawBorderString(spriteBatch, txt, pos, Color.White, scale);
		}

		if (IsMouseHovering)
		{
			Main.LocalPlayer.mouseInterface = true;
			Main.LocalPlayer.cursorItemIconEnabled = false;
			Main.instance.MouseText(BuildTooltip(stack));
			HandleClicks();
		}
		else
		{
			_leftDown = _rightDown = _midDown = false;
		}
	}

	private static string BuildTooltip(FluidStack stack)
	{
		var sb = new System.Text.StringBuilder();
		if (stack.IsEmpty)
		{
			sb.Append("Empty matcher slot\n");
			sb.Append("[c/AAAAAA:LMB / RMB with a fluid container:] set type\n");
			sb.Append("[c/AAAAAA:LMB] = container amount   [c/AAAAAA:RMB] = 1 mB");
		}
		else
		{
			sb.Append(stack.Type!.DisplayName);
			sb.Append("  *  ");
			sb.Append(stack.Amount.ToString("N0"));
			sb.Append(" mB\n");
			sb.Append("[c/AAAAAA:Empty hand  LMB] -1   [c/AAAAAA:RMB] +1\n");
			sb.Append("[c/AAAAAA:Shift+LMB] halve   [c/AAAAAA:Shift+RMB] double\n");
			sb.Append("[c/AAAAAA:Middle-click] clear   [c/AAAAAA:LMB/RMB with container] replace");
		}
		return sb.ToString();
	}

	private static string FormatPhantomAmount(int n)
	{
		if (n < 1000) return n.ToString();
		if (n < 10_000) return (n / 1000f).ToString("0.#") + "k";
		return (n / 1000) + "k";
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

		CoverActions.Send(
			CoverFilterAction.Matcher(_side, fluid: true, _index, button, shift, Main.mouseItem), _entity);
		SoundEngine.PlaySound(SoundID.MenuTick);
	}
}
