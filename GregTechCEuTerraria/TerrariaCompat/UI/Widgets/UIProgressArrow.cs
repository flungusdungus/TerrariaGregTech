#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Horizontal progress arrow bound to a Func<float> (0..1). Texture is a
// vertical stack of two equal-height frames: top = empty, bottom = filled.
// We draw the empty frame in full, then a left-clipped portion of the filled
// frame proportional to progress.
//
// Native upstream arrow is 20 wide x 20 tall per frame (so the asset is
// 20x40). Our widget intrinsic size matches the single frame; layout-scaling
// expands it proportionally.
//
// Sampler: forces PointClamp during the draw - tML's default UI sampler is
// LinearClamp which smears pixel-art edges on upscale (2x display from 20px
// source produces visible blur on the arrow's diagonal). PointClamp gives the
// crisp nearest-neighbor look the upstream art is drawn for.
public sealed class UIProgressArrow : UIElement
{
	private readonly Func<float> _progress;
	private readonly string _assetPath;
	private Asset<Texture2D>? _texture;

	private const int FrameWidth = 20;
	private const int FrameHeight = 20;

	public UIProgressArrow(Func<float> progress, string assetPath = "GregTechCEuTerraria/Content/Textures/gui/progress_bar/progress_bar_arrow")
	{
		_progress = progress;
		_assetPath = assetPath;
		Width = StyleDimension.FromPixels(FrameWidth);
		Height = StyleDimension.FromPixels(FrameHeight);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		_texture ??= ModContent.Request<Texture2D>(_assetPath);
		if (_texture?.Value == null) return;

		var bounds = GetDimensions().ToRectangle();
		var tex = _texture.Value;
		float p = Math.Clamp(_progress(), 0f, 1f);

		PointClampDraw.Draw(spriteBatch, () =>
		{
			var emptySrc = new Rectangle(0, 0, FrameWidth, FrameHeight);
			spriteBatch.Draw(tex, bounds, emptySrc, Color.White);
			if (p > 0f)
			{
				// Advance the fill in whole SOURCE pixels, then scale the dest
				// by an integer factor - so the filled frame samples at the
				// exact same scale as the empty frame. Truncating src and dest
				// independently (the old `(int)(width*p)` pair) drifted their
				// ratio frame-to-frame and made the fill edge shimmer L/R.
				int scale = Math.Max(1, bounds.Width / FrameWidth);
				int srcW  = (int)(FrameWidth * p);
				if (srcW > 0)
				{
					var filledSrc = new Rectangle(0, FrameHeight, srcW, FrameHeight);
					var filledDst = new Rectangle(bounds.X, bounds.Y, srcW * scale, bounds.Height);
					spriteBatch.Draw(tex, filledDst, filledSrc, Color.White);
				}
			}
		});
	}
}
