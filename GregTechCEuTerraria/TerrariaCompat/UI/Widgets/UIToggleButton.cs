#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// 18x18 toggle button - dark bg + centered icon. Reads/writes state via
// getter/setter delegates so the same button class works for any boolean
// machine flag (lock, void, auto-output, etc.).
//
// Visual states: idle (grey), hovering (brighter), on (yellow border + tinted bg).
public sealed class UIToggleButton : UIElement
{
	private readonly string _iconAssetPath;
	private readonly Func<bool> _getter;
	private readonly Action<bool> _setter;
	private readonly string _tooltip;
	private Asset<Texture2D>? _icon;

	// Optional source-rect picker. When set, the draw uses this sub-rect of the
	// icon texture (per ON/OFF state) - mirrors upstream's getSubTexture split
	// for buttons whose ON / OFF artwork is packed into one PNG (e.g.
	// button_distinct_buses.png = top half ON, bottom half OFF). Default null
	// = full texture for both states.
	public Func<bool, Rectangle>? IconSrcRectFor { get; set; }
	// Optional dynamic tooltip - when set, overrides the static `_tooltip`
	// (pressed/unpressed text often differs, matching upstream's
	// `setTooltipsSupplier`).
	public Func<bool, string>? TooltipFor { get; set; }

	public UIToggleButton(string iconAssetPath, Func<bool> getter, Action<bool> setter, string tooltip)
	{
		_iconAssetPath = iconAssetPath;
		_getter = getter;
		_setter = setter;
		_tooltip = tooltip;
		Width = StyleDimension.FromPixels(18);
		Height = StyleDimension.FromPixels(18);
	}

	public override void LeftClick(UIMouseEvent evt)
	{
		base.LeftClick(evt);
		_setter(!_getter());
		SoundEngine.PlaySound(SoundID.MenuTick);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		_icon ??= ModContent.Request<Texture2D>(_iconAssetPath);

		var bounds = GetDimensions().ToRectangle();
		var px = TextureAssets.MagicPixel.Value;
		bool on = _getter();

		// Background tint
		Color bg = on
			? new Color(70, 90, 50) * 0.85f
			: new Color(20, 20, 30) * 0.85f;
		if (IsMouseHovering) bg = Color.Lerp(bg, Color.White, 0.15f);
		spriteBatch.Draw(px, bounds, bg);

		// Icon - pixel-art texture, wrap in PointClamp so the upscale stays
		// crisp at non-integer UI scale ratios.
		if (_icon?.Value != null)
		{
			var t = _icon.Value;
			int inset = System.Math.Max(2, bounds.Width / 9);
			int iconSize = System.Math.Min(bounds.Width, bounds.Height) - inset * 2;
			var iconRect = new Rectangle(
				bounds.X + (bounds.Width - iconSize) / 2,
				bounds.Y + (bounds.Height - iconSize) / 2,
				iconSize, iconSize);
			Color iconTint = on ? Color.White : new Color(170, 170, 170);
			Rectangle? src = IconSrcRectFor?.Invoke(on);
			TerrariaCompat.UI.PointClampDraw.Draw(spriteBatch, () =>
			{
				if (src.HasValue) spriteBatch.Draw(t, iconRect, src.Value, iconTint);
				else              spriteBatch.Draw(t, iconRect, iconTint);
			});
		}

		// Border - yellow when on for emphasis
		var border = on ? new Color(230, 220, 80) : Color.White;
		spriteBatch.Draw(px, new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), border);
		spriteBatch.Draw(px, new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), border);
		spriteBatch.Draw(px, new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), border);
		spriteBatch.Draw(px, new Rectangle(bounds.Right - 1, bounds.Y, 1, bounds.Height), border);

		if (IsMouseHovering)
		{
			// Suppress player item-use / attack-swing while hovering - same
			// trick UIPowerToggle / UISlot / etc. use. Without this the click
			// reaches the player input loop and swings the held item or
			// places a tile through the GUI.
			Main.LocalPlayer.mouseInterface = true;
			string tt = TooltipFor?.Invoke(on) ?? _tooltip;
			if (!string.IsNullOrEmpty(tt))
			{
				Main.instance.MouseText(tt);
				Main.LocalPlayer.cursorItemIconEnabled = false;
			}
		}
	}
}
