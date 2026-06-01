#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent.UI.Elements;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Rounded chest-blue chrome - inherits tML's UIPanel which renders the standard
// 9-slice panel background + border used by vanilla chest / settings menus.
// Subclassing rather than using UIPanel directly keeps the abstraction stable
// in case we later want per-tier color variants or custom frames.
public class UITerrariaPanel : UIPanel
{
	public UITerrariaPanel()
	{
		BackgroundColor = new Color(63, 65, 151) * 0.785f;
		BorderColor = new Color(89, 116, 213) * 0.9f;
		// UIPanel ships with ~12px padding on all sides by default; zero it so
		// widget layout coords are interpreted from the panel's outer edge
		// (otherwise everything shifts inward and right-side widgets clip off).
		PaddingLeft = PaddingRight = PaddingTop = PaddingBottom = 0f;
	}

	// Claim `mouseInterface` whenever the cursor is over the panel rect.
	// Without this, an EMPTY UITerrariaPanel (no child widgets that set
	// mouseInterface themselves) lets world-hover tooltips and item-use
	// bleed through any UI on top of the world. Widget-populated panels
	// usually already set mouseInterface through their hovered widgets;
	// this just makes the chrome itself sufficient.
	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		base.DrawSelf(spriteBatch);
		if (ContainsPoint(Main.MouseScreen))
			Main.LocalPlayer.mouseInterface = true;
	}
}
