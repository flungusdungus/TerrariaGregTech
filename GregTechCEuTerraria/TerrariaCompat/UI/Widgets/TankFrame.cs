#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Thin slot-style border for the fluid widgets - drawn on top of the fluid
// column so a tank reads as a framed slot.
internal static class TankFrame
{
	public const int BorderWidth = 2;

	// Vanilla slot-border tone (#2b1f2b).
	public static readonly Color BorderColor = new(0x2b, 0x1f, 0x2b);

	// Draws a `w`-px border inset along the inside edges of `bounds`.
	public static void DrawBorder(SpriteBatch sb, Rectangle b, Color color, int w = BorderWidth)
	{
		var tex = TextureAssets.MagicPixel.Value;
		sb.Draw(tex, new Rectangle(b.X, b.Y, b.Width, w), color);
		sb.Draw(tex, new Rectangle(b.X, b.Bottom - w, b.Width, w), color);
		sb.Draw(tex, new Rectangle(b.X, b.Y, w, b.Height), color);
		sb.Draw(tex, new Rectangle(b.Right - w, b.Y, w, b.Height), color);
	}
}
