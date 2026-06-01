#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Confirm-style single-line text field. Unfocused shows the external value;
// focused edits a local buffer. Enter / click-away / UnfocusAll commits;
// only Esc discards. XNA polling (same rationale as UISearchBar). `forceUpper`
// uppercases letters regardless of Shift (the ender-channel hex input).
public sealed class UITextField : UIElement
{
	private static UITextField? _focused;

	// Commits before release so closing the popup mid-type saves the input.
	public static void UnfocusAll() => _focused?.Commit();

	private const int InitialRepeatDelay = 25;
	private const int RepeatInterval = 3;

	private readonly Func<string> _current;
	private readonly Action<string> _onConfirm;
	private readonly Func<char, bool>? _filter;
	private readonly int _maxLength;
	private readonly string _placeholder;
	private readonly string? _tooltip;
	private readonly bool _forceUpper;

	private string _buffer = "";
	private KeyboardState _prevKb;
	private Keys _heldKey = Keys.None;
	private int _heldTicks;

	public bool IsFocused => _focused == this;

	public UITextField(Func<string> current, Action<string> onConfirm,
		int maxLength = 32, Func<char, bool>? filter = null, string placeholder = "",
		string? tooltip = null, bool forceUpper = false)
	{
		_current = current;
		_onConfirm = onConfirm;
		_maxLength = maxLength;
		_filter = filter;
		_placeholder = placeholder;
		_tooltip = tooltip;
		_forceUpper = forceUpper;
		OnLeftMouseDown += (_, _) => Focus();
	}

	private void Focus()
	{
		if (IsFocused) return;
		_focused = this;
		_buffer = _current() ?? "";
		_prevKb = Keyboard.GetState();
		_heldKey = Keys.None;
		_heldTicks = 0;
	}

	private void Commit()
	{
		if (!IsFocused) return;
		_focused = null;
		_onConfirm(_buffer);
	}

	private void Discard()
	{
		if (IsFocused) _focused = null;
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		var bounds = GetDimensions().ToRectangle();
		bool over = bounds.Contains((int)Main.MouseScreen.X, (int)Main.MouseScreen.Y);

		if (IsFocused && Main.mouseLeft && !over) Commit();

		if (IsFocused)
		{
			PlayerInput.WritingText = true;   // self-resetting; gates inventory hotkeys
			ProcessKeystrokes();
		}
		if (over)
		{
			Main.LocalPlayer.mouseInterface = true;
			if (_tooltip != null) Main.instance.MouseText(_tooltip);
		}
	}

	private void ProcessKeystrokes()
	{
		var kb = Keyboard.GetState();
		Keys fired = FindFiringKey(kb);
		_prevKb = kb;

		switch (fired)
		{
			case Keys.None: return;
			case Keys.Enter: Commit(); return;
			case Keys.Escape: Discard(); return;
			case Keys.Back:
				if (_buffer.Length > 0) _buffer = _buffer.Substring(0, _buffer.Length - 1);
				return;
		}
		if (_buffer.Length >= _maxLength) return;
		bool shift = kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift);
		if (CharFor(fired, shift) is { } ch && (_filter is null || _filter(ch)))
			_buffer += ch;
	}

	private Keys FindFiringKey(KeyboardState kb)
	{
		foreach (var key in kb.GetPressedKeys())
		{
			if (!_prevKb.IsKeyDown(key))
			{
				_heldKey = key;
				_heldTicks = 0;
				return key;
			}
		}
		if (_heldKey != Keys.None && kb.IsKeyDown(_heldKey))
		{
			_heldTicks++;
			int since = _heldTicks - InitialRepeatDelay;
			return since >= 0 && since % RepeatInterval == 0 ? _heldKey : Keys.None;
		}
		_heldKey = Keys.None;
		return Keys.None;
	}

	private char? CharFor(Keys k, bool shift)
	{
		if (k >= Keys.A && k <= Keys.Z)
		{
			char c = (char)('a' + (k - Keys.A));
			return (shift || _forceUpper) ? char.ToUpperInvariant(c) : c;
		}
		if (k >= Keys.D0 && k <= Keys.D9)
		{
			if (!shift) return (char)('0' + (k - Keys.D0));
			return k switch
			{
				Keys.D1 => '!', Keys.D2 => '@', Keys.D3 => '#', Keys.D4 => '$', Keys.D5 => '%',
				Keys.D6 => '^', Keys.D7 => '&', Keys.D8 => '*', Keys.D9 => '(', Keys.D0 => ')',
				_ => (char?)null,
			};
		}
		if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return (char)('0' + (k - Keys.NumPad0));
		return k switch
		{
			Keys.Space        => ' ',
			Keys.Multiply     => '*',
			Keys.OemSemicolon => shift ? ':' : ';',
			Keys.OemQuestion  => shift ? '?' : '/',
			Keys.OemPipe      => shift ? '|' : '\\',
			Keys.OemMinus     => shift ? '_' : '-',
			Keys.OemPlus      => shift ? '+' : '=',
			Keys.OemPeriod    => shift ? '>' : '.',
			Keys.OemComma     => shift ? '<' : ',',
			_                 => (char?)null,
		};
	}

	protected override void DrawSelf(SpriteBatch sb)
	{
		var bounds = GetDimensions().ToRectangle();
		var px = TextureAssets.MagicPixel.Value;
		sb.Draw(px, bounds, new Color(8, 10, 28) * 0.85f);
		var border = IsFocused ? new Color(255, 235, 140) : new Color(60, 70, 100);
		sb.Draw(px, new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), border);
		sb.Draw(px, new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), border);
		sb.Draw(px, new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), border);
		sb.Draw(px, new Rectangle(bounds.Right - 1, bounds.Y, 1, bounds.Height), border);

		string shown = IsFocused ? _buffer : (_current() ?? "");
		bool empty = shown.Length == 0;
		var font = FontAssets.MouseText.Value;
		const float scale = 0.8f;
		float ty = bounds.Y + (bounds.Height - font.LineSpacing * scale) / 2f - 1;
		Terraria.Utils.DrawBorderString(sb, empty ? _placeholder : shown,
			new Vector2(bounds.X + 5, ty), empty ? new Color(140, 140, 160) : Color.White, scale);

		if (IsFocused && Main.GameUpdateCount % 30 < 15)
		{
			float w = empty ? 0f : font.MeasureString(shown).X * scale;
			Terraria.Utils.DrawBorderString(sb, "|", new Vector2(bounds.X + 5 + w, ty),
				Color.LightYellow, scale);
		}
	}
}
