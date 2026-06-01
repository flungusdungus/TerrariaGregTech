#nullable enable
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// SSOT for display-only fluid-slot rendering + hover tooltip across browser
// surfaces (recipe rows, favorites, ...). Live machine widgets (UIFluidSlot
// etc.) stay separate - they carry click/fill behaviour. fallbackLabel covers a
// null fluid (unresolved tag/attr); EmitTooltip's extraLine is appended verbatim
// so callers attach chance/container notes without forking the helper.
public static class BrowserFluidSlot
{
	private const int LabelHeight = 10;

	public static void Draw(SpriteBatch sb, Rectangle dest, FluidType? fluid,
		int amountMb = 0, string? fallbackLabel = null, Color? lightColor = null)
	{
		var tint = lightColor ?? Color.White;
		float alpha = tint.A / 255f;
		var px = TextureAssets.MagicPixel.Value;

		sb.Draw(px, dest, new Color(20, 25, 50) * alpha);

		var inner = new Rectangle(dest.X + 2, dest.Y + 2, dest.Width - 4, dest.Height - 4);
		if (fluid is null || !FluidIconRenderer.Draw(sb, fluid, inner, alpha))
		{
			Color fallback = new(80, 80, 200);
			if (fluid is not null)
			{
				uint c = fluid.Color;
				fallback = new Color((byte)((c >> 16) & 0xFF),
					(byte)((c >> 8) & 0xFF), (byte)(c & 0xFF));
			}
			sb.Draw(px, inner, fallback * alpha);
		}

		TankFrame.DrawBorder(sb, dest, TankFrame.BorderColor * alpha);

		string label = fluid?.DisplayName ?? fallbackLabel ?? "?";
		if (label.Length > 0)
		{
			Terraria.Utils.DrawBorderString(sb,
				label.Substring(0, System.Math.Min(2, label.Length)).ToUpperInvariant(),
				new Vector2(dest.X + 2, dest.Y + 2),
				tint, 0.6f);
		}

		if (amountMb > 0)
		{
			Terraria.Utils.DrawBorderString(sb, amountMb.ToString(),
				new Vector2(dest.X + 2, dest.Bottom - LabelHeight),
				tint, 0.6f);
		}
	}

	public static void EmitTooltip(FluidType? fluid, int amountMb = 0,
		string? fallbackLabel = null, string? extraLine = null)
	{
		string name = fluid?.DisplayName ?? fallbackLabel ?? "?";
		string body = amountMb > 0 ? $"{name}\n{amountMb} mB" : name;
		if (!string.IsNullOrEmpty(extraLine)) body += extraLine;
		Main.instance.MouseText(body);
	}
}
