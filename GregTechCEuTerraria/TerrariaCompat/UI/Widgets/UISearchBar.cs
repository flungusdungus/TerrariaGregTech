#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// JEI-style text input. LMB=focus, RMB=clear, Esc/Enter=unfocus, key-repeat
// after InitialRepeatDelay every RepeatInterval.
//
// **Direct XNA polling, not Main.GetInputText** - vanilla's text-input pipeline
// doesn't deliver keystrokes to a mod UIElement (its focus routing is hard-coded
// to chat/sign/NPC text). Tradeoff: no clipboard paste, no arrows, no IME.
//
// **UnfocusAll() is static** - hosting UIStates must call it on close, else
// focus leaks across UI lifetimes (next typed key lands in an invisible widget).
//
// **Never touch Main.blockInput** - vanilla never resets it; setting once
// soft-locks the player past the UI closing.
public sealed class UISearchBar : UIElement
{
	private static UISearchBar? _focusedInstance;

	public static void UnfocusAll() => _focusedInstance = null;

	// XNA polling doesn't auto-repeat.
	private const int InitialRepeatDelay = 25;   // ~0.4 s
	private const int RepeatInterval     = 3;    // ~12 keys/sec

	private string _text = "";
	private readonly string _placeholder;
	private readonly Action<string> _onChanged;
	private KeyboardState _prevKb;
	private Keys _heldKey = Keys.None;
	private int _heldTicks;

	public string Text => _text;
	public bool IsFocused => _focusedInstance == this;

	public UISearchBar(string placeholder, Action<string> onChanged)
	{
		_placeholder = placeholder;
		_onChanged = onChanged;
		OnLeftMouseDown  += (_, _) => Focus();
		OnRightMouseDown += (_, _) => { SetText(""); Focus(); };
	}

	private void Focus()
	{
		_focusedInstance = this;
		// Snapshot so already-held click modifiers don't re-fire as new presses.
		_prevKb = Keyboard.GetState();
		_heldKey = Keys.None;
		_heldTicks = 0;
	}

	public void Unfocus() { if (IsFocused) _focusedInstance = null; }

	public void SetText(string text)
	{
		if (_text == text) return;
		_text = text;
		_onChanged(_text);
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);

		var bounds = GetDimensions().ToRectangle();
		bool over = bounds.Contains((int)Main.MouseScreen.X, (int)Main.MouseScreen.Y);

		if (IsFocused && Main.mouseLeft && !over) Unfocus();

		if (IsFocused)
		{
			// WritingText is per-frame self-resetting - safe to leave set.
			PlayerInput.WritingText = true;
			ProcessKeystrokes();
		}

		if (over) Main.LocalPlayer.mouseInterface = true;
	}

	private void ProcessKeystrokes()
	{
		var kb = Keyboard.GetState();
		Keys fired = FindFiringKey(kb);

		if (fired == Keys.Escape || fired == Keys.Enter) { Unfocus(); _prevKb = kb; return; }
		if (fired == Keys.Back)
		{
			if (_text.Length > 0) SetText(_text.Substring(0, _text.Length - 1));
		}
		else if (fired != Keys.None)
		{
			bool shift = kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift);
			// Console.CapsLock is Windows-only; Shift covers other OS.
			bool caps  = System.OperatingSystem.IsWindows() && Console.CapsLock;
			if (CharFor(fired, shift, caps) is { } ch) SetText(_text + ch);
		}

		_prevKb = kb;
	}

	// Key firing this frame - newly-pressed, or held key whose repeat is due.
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
			int sinceRepeatStart = _heldTicks - InitialRepeatDelay;
			if (sinceRepeatStart >= 0 && sinceRepeatStart % RepeatInterval == 0)
				return _heldKey;
			return Keys.None;
		}

		_heldKey = Keys.None;
		return Keys.None;
	}

	// Letters / digits handled by range arithmetic; Oem keys explicit.
	private static readonly Dictionary<Keys, (string Plain, string Shift)> Punct = new()
	{
		{ Keys.Space,            (" ",  " ")  },
		{ Keys.OemMinus,         ("-",  "_")  },
		{ Keys.OemPlus,          ("=",  "+")  },
		{ Keys.OemPeriod,        (".",  ">")  },
		{ Keys.OemComma,         (",",  "<")  },
		{ Keys.OemQuestion,      ("/",  "?")  },
		{ Keys.OemSemicolon,     (";",  ":")  },
		{ Keys.OemQuotes,        ("'",  "\"") },
		{ Keys.OemTilde,         ("`",  "~")  },
		{ Keys.OemPipe,          ("\\", "|")  },
		{ Keys.OemOpenBrackets,  ("[",  "{")  },
		{ Keys.OemCloseBrackets, ("]",  "}")  },
	};
	private static readonly string[] ShiftDigits = { ")", "!", "@", "#", "$", "%", "^", "&", "*", "(" };

	private static string? CharFor(Keys k, bool shift, bool caps)
	{
		if (k >= Keys.A && k <= Keys.Z)
		{
			char c = (char)('a' + (k - Keys.A));
			return (shift ^ caps) ? char.ToUpperInvariant(c).ToString() : c.ToString();
		}
		if (k >= Keys.D0 && k <= Keys.D9)        return shift ? ShiftDigits[k - Keys.D0] : ((int)(k - Keys.D0)).ToString();
		if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return ((int)(k - Keys.NumPad0)).ToString();
		if (Punct.TryGetValue(k, out var p))     return shift ? p.Shift : p.Plain;
		return null;
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var bounds = GetDimensions().ToRectangle();
		var px = TextureAssets.MagicPixel.Value;

		spriteBatch.Draw(px, bounds, new Color(8, 10, 28) * 0.85f);
		DrawBorder(spriteBatch, px, bounds, IsFocused ? new Color(255, 235, 140) : new Color(60, 70, 100));

		var font = FontAssets.MouseText.Value;
		const float scale = 0.85f;
		float textY = bounds.Y + (bounds.Height - font.LineSpacing * scale) / 2f - 1;
		bool empty = _text.Length == 0;

		Terraria.Utils.DrawBorderString(spriteBatch,
			empty ? _placeholder : _text,
			new Vector2(bounds.X + 6, textY),
			empty ? new Color(140, 140, 160) : Color.White,
			scale);

		if (IsFocused && (Main.GameUpdateCount % 30) < 15)
		{
			float w = empty ? 0f : font.MeasureString(_text).X * scale;
			Terraria.Utils.DrawBorderString(spriteBatch, "|",
				new Vector2(bounds.X + 6 + w, textY),
				Color.LightYellow, scale);
		}
	}

	private static void DrawBorder(SpriteBatch sb, Texture2D px, Rectangle b, Color c)
	{
		sb.Draw(px, new Rectangle(b.X, b.Y, b.Width, 1), c);
		sb.Draw(px, new Rectangle(b.X, b.Bottom - 1, b.Width, 1), c);
		sb.Draw(px, new Rectangle(b.X, b.Y, 1, b.Height), c);
		sb.Draw(px, new Rectangle(b.Right - 1, b.Y, 1, b.Height), c);
	}
}
