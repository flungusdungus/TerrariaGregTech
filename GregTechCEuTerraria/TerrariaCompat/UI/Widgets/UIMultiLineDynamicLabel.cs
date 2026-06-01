#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;
using Terraria.UI.Chat;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Multi-line dynamic label - re-evaluates a `Func<List<string>>` each draw
// and stacks the lines vertically. The display analogue of upstream's
// `ComponentPanelWidget` (which iterates a textSupplier-produced
// `List<Component>` into rows). Used by every multiblock layout that drives
// its status text through `MultiblockDisplayText.Create(textList, ...)` so
// the layout writes ONE widget and the builder owns the line set.
public sealed class UIMultiLineDynamicLabel : UIElement
{
	private readonly Func<IReadOnlyList<string>> _getter;
	private readonly float _scale;
	private readonly float _lineHeight;

	public UIMultiLineDynamicLabel(Func<IReadOnlyList<string>> getter, float scale = 0.85f, float lineHeight = 16f)
	{
		_getter = getter;
		_scale  = scale;
		_lineHeight = lineHeight;
		Width  = StyleDimension.FromPixels(300);
		Height = StyleDimension.FromPixels(200);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var b = GetDimensions();
		var font = FontAssets.MouseText.Value;
		var lines = _getter();
		float y = b.Y;
		// Wrap to the widget width. WordwrapStringSmart measures in font-space at
		// scale 1, but we draw at _scale, so the budget is width/_scale. Long
		// status lines (e.g. "Wrong block at (x,y): expected ... (found: ...)") now
		// fold onto multiple rows instead of running off the screen.
		int maxWidth = Math.Max(40, (int)(b.Width / _scale));
		for (int i = 0; i < lines.Count; i++)
		{
			string line = lines[i];
			if (string.IsNullOrEmpty(line))
			{
				// Blank line still consumes vertical space - mirrors
				// upstream's empty `Component.empty()` rows for spacing.
				y += _lineHeight;
				continue;
			}
			// Tag-aware wrap: parses [c/RRGGBB:] color tags AND folds to width,
			// returning one snippet-list per visual row.
			var wrapped = Terraria.Utils.WordwrapStringSmart(line, Color.White, font, maxWidth, 20);
			foreach (var snippetLine in wrapped)
			{
				ChatManager.DrawColorCodedStringWithShadow(
					spriteBatch, font, snippetLine.ToArray(), new Vector2(b.X, y),
					0f, Vector2.Zero, new Vector2(_scale), out _, -1f);
				y += _lineHeight;
			}
		}
	}
}
