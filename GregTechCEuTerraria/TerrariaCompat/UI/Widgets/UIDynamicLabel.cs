#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Re-evaluates its text every draw - used for live values like fluid amount,
// EU stored, recipe progress. Caller passes a Func<string> closed over the
// entity reference.
public sealed class UIDynamicLabel : UIElement
{
	private readonly Func<string> _getter;
	private readonly float _scale;

	public UIDynamicLabel(Func<string> getter, float scale = 0.85f)
	{
		_getter = getter;
		_scale = scale;
		Width = StyleDimension.FromPixels(120);
		Height = StyleDimension.FromPixels(16);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var b = GetDimensions();
		Terraria.Utils.DrawBorderString(spriteBatch, _getter(), new Vector2(b.X, b.Y), Color.White, _scale);
	}
}
