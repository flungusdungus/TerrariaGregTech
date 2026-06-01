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

// Generic text button - a bordered rect with a centered label, running a
// callback on click. The label is a Func so it always reflects live state
// (a cover setting's current value). Left- and right-click get separate
// callbacks, so it doubles as a cycle / stepper control: L = next / +,
// R = prev / -.
public sealed class UITextButton : UIElement
{
	private readonly Func<string> _label;
	private readonly Action? _onLeft;
	private readonly Action? _onRight;
	private readonly string? _tooltip;
	private bool _leftDown, _rightDown;

	// Optional radio-group highlight - when set + returns true, the button
	// renders with the yellow border + green-tinted bg shared with
	// UIToggleButton's ON state. Used by the pipe-settings panel's mode +
	// filter-mode rows to mark the currently-selected option.
	public Func<bool>? IsActive { get; set; }

	// Optional disabled flag - when set + returns true, the button is greyed
	// out, refuses clicks, and shows the disabled tooltip if provided.
	public Func<bool>? IsDisabled { get; set; }
	public string? DisabledTooltip { get; set; }

	// Optional visibility predicate - when set + returns false, the button is
	// not drawn AND refuses hit-testing. Different from IsDisabled (which
	// greys out but keeps the button visible). Used by multi GUI layouts to
	// hide control buttons (e.g. boiler throttle +/-) while the structure is
	// unformed.
	public Func<bool>? IsVisible { get; set; }

	public UITextButton(Func<string> label, Action? onLeft = null, Action? onRight = null,
		string? tooltip = null, int width = 64, int height = 18)
	{
		_label = label;
		_onLeft = onLeft;
		_onRight = onRight;
		_tooltip = tooltip;
		Width = StyleDimension.FromPixels(width);
		Height = StyleDimension.FromPixels(height);
	}

	public override bool ContainsPoint(Vector2 point)
	{
		// Hidden buttons swallow no clicks - lets the layout draw them in the
		// same screen rect as another widget that should receive the click
		// when the button is hidden.
		if (IsVisible?.Invoke() == false) return false;
		return base.ContainsPoint(point);
	}

	protected override void DrawSelf(SpriteBatch sb)
	{
		if (IsVisible?.Invoke() == false) return;
		bool disabled = IsDisabled?.Invoke() == true;
		bool active = !disabled && IsActive?.Invoke() == true;

		var bounds = GetDimensions().ToRectangle();
		var px = TextureAssets.MagicPixel.Value;

		Color bg = disabled
			? new Color(28, 28, 32) * 0.85f
			: active
				? new Color(70, 90, 50) * 0.92f                                                 // matches UIToggleButton ON
				: new Color(50, 52, 110) * 0.92f;
		Color border = disabled
			? new Color(70, 70, 75)
			: active
				? new Color(230, 220, 80)                                                       // yellow ON border
				: (IsMouseHovering ? new Color(125, 145, 235) : new Color(89, 116, 213)) * 0.9f;
		sb.Draw(px, bounds, bg);
		sb.Draw(px, new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), border);
		sb.Draw(px, new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), border);
		sb.Draw(px, new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), border);
		sb.Draw(px, new Rectangle(bounds.Right - 1, bounds.Y, 1, bounds.Height), border);

		string text = _label();
		var font = FontAssets.MouseText.Value;
		const float scale = 0.72f;
		var size = font.MeasureString(text) * scale;
		var pos = new Vector2(
			bounds.X + (bounds.Width - size.X) / 2f,
			bounds.Y + (bounds.Height - size.Y) / 2f);
		Color textColor = disabled ? new Color(140, 140, 145) : Color.White;
		Terraria.Utils.DrawBorderString(sb, text, pos, textColor, scale);

		if (IsMouseHovering)
		{
			Main.LocalPlayer.mouseInterface = true;
			string? tt = disabled ? (DisabledTooltip ?? _tooltip) : _tooltip;
			if (tt != null) Main.instance.MouseText(tt);
			if (!disabled) HandleClicks();
		}

		// Track press edges unconditionally so a click that started off the
		// button doesn't fire on the frame the cursor moves onto it.
		_leftDown = Main.mouseLeft;
		_rightDown = Main.mouseRight;
	}

	private void HandleClicks()
	{
		if (Main.mouseLeft && !_leftDown && _onLeft != null)
		{
			_onLeft();
			SoundEngine.PlaySound(SoundID.MenuTick);
		}
		if (Main.mouseRight && !_rightDown && _onRight != null)
		{
			_onRight();
			SoundEngine.PlaySound(SoundID.MenuTick);
		}
	}
}
