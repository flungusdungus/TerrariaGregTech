#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine.Steam;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Vertical temperature bar for a steam boiler - same vocabulary as UIEnergyBar
// / UIFluidSlot (dark bg + filled column rising from the bottom + framed
// border + hover tooltip). Mirrors upstream SteamBoilerMachine.createUI's
// `ProgressWidget(getTemperaturePercent, 10x54, DOWN_TO_UP)`. The fill colour
// ramps from a dim ember to bright heat as the temperature climbs.
public sealed class UITemperatureBar : UIElement
{
	private readonly SteamBoilerMachine _boiler;

	public UITemperatureBar(SteamBoilerMachine boiler, int width, int height)
	{
		_boiler = boiler;
		Width = StyleDimension.FromPixels(width);
		Height = StyleDimension.FromPixels(height);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var bounds = GetDimensions().ToRectangle();
		var tex = TextureAssets.MagicPixel.Value;

		// Dark background
		spriteBatch.Draw(tex, bounds, new Color(20, 20, 30) * 0.85f);

		// Heat column - rises from the bottom, colour ramps cold->hot.
		float fill = System.Math.Clamp(_boiler.TempProgress01, 0f, 1f);
		if (fill > 0f)
		{
			int fillH = (int)(bounds.Height * fill);
			if (fillH > 0)
			{
				var fillRect = new Rectangle(bounds.X, bounds.Bottom - fillH, bounds.Width, fillH);
				var hot = Color.Lerp(new Color(120, 40, 10), new Color(255, 210, 70), fill);
				spriteBatch.Draw(tex, fillRect, hot);
			}
		}

		// Slot-style border, matching the water/steam tanks beside it.
		TankFrame.DrawBorder(spriteBatch, bounds, TankFrame.BorderColor);

		if (IsMouseHovering)
		{
			Main.instance.MouseText(
				$"Temperature: {_boiler.CurrentTemperature} / {_boiler.GetMaxTemperature()}  ({fill * 100:F0}%)");
			Main.LocalPlayer.cursorItemIconEnabled = false;
		}
	}
}
