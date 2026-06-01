#nullable enable
using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Pixel-perfect draw wrapper that preserves ALL other SpriteBatch state.
//
// The naive approach (sb.End + sb.Begin(...PointClamp...) + body + sb.End +
// sb.Begin(...vanilla state...)) lost scissor clipping because we had to guess
// at the vanilla rasterizer/matrix state and got it wrong in subtle ways.
//
// The robust approach: read XNA's private cached Begin params off the
// SpriteBatch via reflection, swap only the SamplerState to PointClamp,
// then restore the exact original params after. Nothing else changes -
// rasterizer (incl. scissor), blend, depth, transform matrix, sort mode,
// and effect all come back identical to what vanilla had.
//
// Used by item PreDraw* overrides where we draw 16-px upstream sprites
// at 2x (`Item.scale=2`-style scaling): vanilla's item-pass uses
// LinearClamp which would blur the upscale, so we need PointClamp on the
// draw call, but with vanilla's clipping intact.
public static class PointClampDraw
{
	private static readonly FieldInfo F_SortMode    = Get("sortMode");
	private static readonly FieldInfo F_Blend       = Get("blendState");
	private static readonly FieldInfo F_Sampler     = Get("samplerState");
	private static readonly FieldInfo F_DepthStencil= Get("depthStencilState");
	private static readonly FieldInfo F_Rasterizer  = Get("rasterizerState");
	private static readonly FieldInfo F_Effect      = Get("customEffect");
	private static readonly FieldInfo F_Matrix      = Get("transformMatrix");

	private static FieldInfo Get(string name) =>
		typeof(SpriteBatch).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
		?? throw new InvalidOperationException(
			$"SpriteBatch field `{name}` not found - XNA/FNA internals changed");

	public static void Draw(SpriteBatch sb, Action body)
	{
		// Snapshot current params
		var sortMode     = (SpriteSortMode)F_SortMode.GetValue(sb)!;
		var blendState   = (BlendState)F_Blend.GetValue(sb)!;
		var sampler      = (SamplerState)F_Sampler.GetValue(sb)!;
		var depthStencil = (DepthStencilState)F_DepthStencil.GetValue(sb)!;
		var rasterizer   = (RasterizerState)F_Rasterizer.GetValue(sb)!;
		var effect       = (Effect?)F_Effect.GetValue(sb);
		var matrix       = (Matrix)F_Matrix.GetValue(sb)!;

		sb.End();
		sb.Begin(sortMode, blendState, SamplerState.PointClamp,
			depthStencil, rasterizer, effect, matrix);
		try { body(); }
		finally
		{
			sb.End();
			sb.Begin(sortMode, blendState, sampler,
				depthStencil, rasterizer, effect, matrix);
		}
	}

	// Convenience: UI-context overload, kept for callers (MachineUIState.Draw)
	// that don't have a per-call matrix opinion. Same behavior as Draw(sb, body)
	// since we snapshot whatever's active.
	public static void Draw(SpriteBatch sb, Matrix _unused, Action body) => Draw(sb, body);
}
