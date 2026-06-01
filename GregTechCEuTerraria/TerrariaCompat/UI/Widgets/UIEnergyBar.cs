#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.Api.Capability;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Vertical EU bar bound to an TieredEnergyMachine. Same vocabulary as UIFluidSlot
// - dark bg + filled column + outline + hover tooltip - tinted yellow for EU.
public sealed class UIEnergyBar : UIElement
{
	private readonly TieredEnergyMachine _container;

	public UIEnergyBar(TieredEnergyMachine container, int width, int height)
	{
		_container = container;
		Width = StyleDimension.FromPixels(width);
		Height = StyleDimension.FromPixels(height);
	}

	protected override void DrawSelf(SpriteBatch spriteBatch)
	{
		var bounds = GetDimensions().ToRectangle();
		var tex = TextureAssets.MagicPixel.Value;

		spriteBatch.Draw(tex, bounds, new Color(20, 20, 30) * 0.85f);

		long stored = _container.EnergyStored;
		long cap = _container.EnergyCapacity;
		if (cap > 0 && stored > 0)
		{
			float fill = System.Math.Clamp((float)stored / cap, 0f, 1f);
			int fillH = (int)(bounds.Height * fill);
			var fillRect = new Rectangle(bounds.X, bounds.Y + bounds.Height - fillH, bounds.Width, fillH);
			spriteBatch.Draw(tex, fillRect, new Color(240, 200, 50));
		}

		var c = Color.White;
		spriteBatch.Draw(tex, new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), c);
		spriteBatch.Draw(tex, new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), c);
		spriteBatch.Draw(tex, new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), c);
		spriteBatch.Draw(tex, new Rectangle(bounds.Right - 1, bounds.Y, 1, bounds.Height), c);

		if (IsMouseHovering)
		{
			var ec   = _container.EnergyContainer;
			var tier = _container.Tier;
			string text = $"{stored:N0} / {cap:N0} EU"
			            + $"\n{VoltageTiers.ShortName(tier)} - {VoltageTiers.Voltage(tier):N0} EU/t";
			// Show whichever I/O sides the container actually has (a receiver
			// machine shows only Input, a generator only Output, a battery
			// buffer / transformer both).
			if (ec.InputVoltage > 0)
				text += $"\nIn: {ec.InputAmperage}A @ {ec.InputVoltage:N0} EU/t";
			if (ec.OutputVoltage > 0)
				text += $"\nOut: {ec.OutputAmperage}A @ {ec.OutputVoltage:N0} EU/t";
			Main.instance.MouseText(text);
			Main.LocalPlayer.cursorItemIconEnabled = false;
		}
	}
}
