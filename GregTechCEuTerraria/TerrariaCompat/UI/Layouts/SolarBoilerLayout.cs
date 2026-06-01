#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine.Steam;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Solar Boiler GUI - port of SteamSolarBoiler's createUI. The same
// water / steam / temperature columns as the coal boiler, but no fuel/ash
// slots and no progress arrow (no fuel recipe) - a sunlit indicator instead.
public static class SolarBoilerLayout
{
	public static MachineUILayout Build(SteamSolarBoiler boiler) => new()
	{
		Width  = 176,
		Height = 100,
		Title  = boiler.DisplayName,

		Widgets =
		{
			// Steam tank (output) - R-click empty bucket to drain.
			new LabelWidgetSpec(X: 6, Y: 18, Text: "Steam", Scale: 0.7f),
			new FluidSlotWidgetSpec(X: 6, Y: 28, Width: 14, Height: 54, Direction: IO.OUT, TankIndex: 0),

			// Water tank (input) - R-click water bucket to fill. (Drain blocked
			// by SteamBoilerMachine.GetTankClickCaps - upstream parity.)
			new LabelWidgetSpec(X: 24, Y: 18, Text: "Water", Scale: 0.7f),
			new FluidSlotWidgetSpec(X: 24, Y: 28, Width: 14, Height: 54, Direction: IO.IN, TankIndex: 0),

			// Temperature bar - cold->hot vertical fill.
			new LabelWidgetSpec(X: 42, Y: 18, Text: "Temp", Scale: 0.7f),
			new TemperatureBarWidgetSpec(X: 42, Y: 28, Width: 14, Height: 54),

			// Sunlit indicator - the solar boiler heats only while it can see sky.
			new DynamicLabelWidgetSpec(X: 72, Y: 28,
				Getter: () => boiler.IsSunlit ? "Sunlit - heating" : "No sunlight", Scale: 0.7f),

			new DynamicLabelWidgetSpec(X: 72, Y: 44, Getter: () =>
			{
				var water = boiler.GetTank(0);
				var steam = boiler.GetTank(1);
				return $"Temp  {boiler.CurrentTemperature}/{boiler.GetMaxTemperature()}\n" +
				       $"Steam {(steam.IsEmpty ? 0 : steam.Amount):N0}mB\n" +
				       $"Water {(water.IsEmpty ? 0 : water.Amount):N0}mB";
			}, Scale: 0.6f),
		},
	};
}
