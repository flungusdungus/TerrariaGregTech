#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Inline `Label  [-] 1234 [+]` numeric stepper. Holding Shift on a button
// multiplies the step by 10, holding Ctrl by 100 (vanilla convention - same
// modifier shape `Player.lockGoldChest` etc. use). RMB on a button applies
// the opposite-sign step, mirroring the upstream creative machine's `+/-5%`
// throttle convention.
public sealed class UINumericStepper : UIElement
{
	private readonly string _label;
	private readonly Func<long> _getter;
	private readonly Action<long> _setter;
	private readonly long _min, _max, _step;
	private readonly int _labelWidth;
	private bool _leftDown, _rightDown;

	public UINumericStepper(string label, Func<long> getter, Action<long> setter,
		long min, long max, long step, int labelWidth)
	{
		_label  = label;
		_getter = getter;
		_setter = setter;
		_min    = min;
		_max    = max;
		_step   = step;
		_labelWidth = labelWidth;
		Width  = StyleDimension.FromPixels(labelWidth + 100);
		Height = StyleDimension.FromPixels(16);
	}

	protected override void DrawSelf(SpriteBatch sb)
	{
		var bounds = GetDimensions().ToRectangle();
		var font = FontAssets.MouseText.Value;
		const float scale = 0.72f;

		// Label.
		Terraria.Utils.DrawBorderString(sb, _label,
			new Vector2(bounds.X, bounds.Y + 2), Color.White, scale);

		// `[-]` button.
		var minusRect = new Rectangle(bounds.X + _labelWidth,      bounds.Y, 16, 16);
		var valueRect = new Rectangle(bounds.X + _labelWidth + 18, bounds.Y, 64, 16);
		var plusRect  = new Rectangle(bounds.X + _labelWidth + 84, bounds.Y, 16, 16);
		DrawButton(sb, minusRect, "-");
		// Readout - centered.
		long val = _getter();
		string text = val.ToString("N0");
		var size = font.MeasureString(text) * scale;
		Terraria.Utils.DrawBorderString(sb, text,
			new Vector2(valueRect.X + (valueRect.Width - size.X) / 2f, valueRect.Y + 1),
			Color.White, scale);
		DrawButton(sb, plusRect, "+");

		// Click handling.
		var ms = Main.MouseScreen;
		bool leftPress  = Main.mouseLeft  && !_leftDown;
		bool rightPress = Main.mouseRight && !_rightDown;
		_leftDown  = Main.mouseLeft;
		_rightDown = Main.mouseRight;

		if (!leftPress && !rightPress) return;

		long mult = 1;
		if (Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift)) mult *= 10;
		if (Main.keyState.IsKeyDown(Keys.LeftControl) || Main.keyState.IsKeyDown(Keys.RightControl)) mult *= 100;

		if (minusRect.Contains((int)ms.X, (int)ms.Y))
		{
			Apply(-(_step * mult * (rightPress ? -1 : 1)));
			SoundEngine.PlaySound(SoundID.MenuTick);
		}
		else if (plusRect.Contains((int)ms.X, (int)ms.Y))
		{
			Apply(_step * mult * (rightPress ? -1 : 1));
			SoundEngine.PlaySound(SoundID.MenuTick);
		}
	}

	private void Apply(long delta)
	{
		long v = _getter() + delta;
		if (v < _min) v = _min;
		if (v > _max) v = _max;
		_setter(v);
	}

	private static void DrawButton(SpriteBatch sb, Rectangle rect, string label)
	{
		var px = TextureAssets.MagicPixel.Value;
		sb.Draw(px, rect, new Color(50, 52, 110) * 0.92f);
		sb.Draw(px, new Rectangle(rect.X, rect.Y, rect.Width, 1),         new Color(89, 116, 213) * 0.9f);
		sb.Draw(px, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), new Color(89, 116, 213) * 0.9f);
		sb.Draw(px, new Rectangle(rect.X, rect.Y, 1, rect.Height),         new Color(89, 116, 213) * 0.9f);
		sb.Draw(px, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), new Color(89, 116, 213) * 0.9f);
		var font = FontAssets.MouseText.Value;
		const float scale = 0.72f;
		var size = font.MeasureString(label) * scale;
		Terraria.Utils.DrawBorderString(sb, label,
			new Vector2(rect.X + (rect.Width - size.X) / 2f, rect.Y + 1),
			Color.White, scale);
	}
}
