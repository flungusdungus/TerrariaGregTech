#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Steam;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Liquid Boiler GUI - port of SteamLiquidBoilerMachine's createUI: the coal
// boiler's water / steam / temperature columns, plus a liquid-fuel tank (no
// item fuel/ash slots - the fuel is a fluid).
public static class LiquidBoilerLayout
{
	public static MachineUILayout Build(SteamLiquidBoilerMachine boiler) => new()
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

			// Liquid-fuel tank (input) - R-click a fuel bucket/cell to fill
			// (creosote / lava). TankIndex 1 = the 2nd input fluid tank.
			new LabelWidgetSpec(X: 60, Y: 18, Text: "Fuel", Scale: 0.7f),
			new FluidSlotWidgetSpec(X: 60, Y: 28, Width: 14, Height: 54, Direction: IO.IN, TankIndex: 1),

			// Live status.
			new DynamicLabelWidgetSpec(X: 88, Y: 28,
				Getter: () => RecipeStatusText.StatusLine(boiler.Recipe, "Burning"), Scale: 0.65f),

			new DynamicLabelWidgetSpec(X: 88, Y: 50, Getter: () =>
			{
				var water = boiler.GetTank(0);
				var steam = boiler.GetTank(1);
				var fuel  = boiler.GetTank(2);
				return $"Steam {(steam.IsEmpty ? 0 : steam.Amount):N0}mB\n" +
				       $"Water {(water.IsEmpty ? 0 : water.Amount):N0}mB\n" +
				       $"Fuel  {(fuel.IsEmpty  ? 0 : fuel.Amount):N0}mB";
			}, Scale: 0.6f),
		},
	};
}
