#nullable enable
using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Runs a draw body clipped to a screen-pixel rectangle, preserving every other
// SpriteBatch param. Same robustness approach as PointClampDraw: snapshot XNA's
// private cached Begin params via reflection, swap in a scissor-enabled
// rasterizer for the body, then restore the originals exactly.
//
// Used by the questbook node canvas (QuestGraph) so panned/zoomed nodes don't
// bleed past the canvas region.
public static class ScissorDraw
{
	private static readonly FieldInfo F_SortMode     = Get("sortMode");
	private static readonly FieldInfo F_Blend        = Get("blendState");
	private static readonly FieldInfo F_Sampler      = Get("samplerState");
	private static readonly FieldInfo F_DepthStencil = Get("depthStencilState");
	private static readonly FieldInfo F_Rasterizer   = Get("rasterizerState");
	private static readonly FieldInfo F_Effect       = Get("customEffect");
	private static readonly FieldInfo F_Matrix       = Get("transformMatrix");

	private static FieldInfo Get(string name) =>
		typeof(SpriteBatch).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
		?? throw new InvalidOperationException(
			$"SpriteBatch field `{name}` not found - XNA/FNA internals changed");

	public static void Draw(SpriteBatch sb, Rectangle clip, Action body)
	{
		var sortMode     = (SpriteSortMode)F_SortMode.GetValue(sb)!;
		var blendState   = (BlendState)F_Blend.GetValue(sb)!;
		var sampler      = (SamplerState)F_Sampler.GetValue(sb)!;
		var depthStencil = (DepthStencilState)F_DepthStencil.GetValue(sb)!;
		var rasterizer   = (RasterizerState)F_Rasterizer.GetValue(sb)!;
		var effect       = (Effect?)F_Effect.GetValue(sb);
		var matrix       = (Matrix)F_Matrix.GetValue(sb)!;

		GraphicsDevice device = sb.GraphicsDevice;
		Rectangle previous = device.ScissorRectangle;

		sb.End();
		var scissorRasterizer = new RasterizerState { CullMode = CullMode.None, ScissorTestEnable = true };
		device.ScissorRectangle = Rectangle.Intersect(previous, clip);
		sb.Begin(sortMode, blendState, sampler, depthStencil, scissorRasterizer, effect, matrix);
		try
		{
			body();
		}
		finally
		{
			sb.End();
			device.ScissorRectangle = previous;
			sb.Begin(sortMode, blendState, sampler, depthStencil, rasterizer, effect, matrix);
		}
	}
}
