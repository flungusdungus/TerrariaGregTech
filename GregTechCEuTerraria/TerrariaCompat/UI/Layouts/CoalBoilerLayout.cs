#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.UI.Widgets;
using GregTechCEuTerraria.TerrariaCompat.Machine.Steam;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Coal Boiler GUI - upstream-parity port of SteamSolidBoilerMachine.createUI.
// Steam/Water/Temp columns + ash-over-fuel with a progress arrow between.
// Coords are 16px MC units, scaled x2 by MachineUILayout.Scale.
public static class CoalBoilerLayout
{
	public static MachineUILayout Build(SteamSolidBoilerMachine boiler) => new()
	{
		Width = 176,
		Height = 100,
		Title = boiler.IsHighPressure ? "High Pressure Coal Boiler" : "Coal Boiler",

		Widgets =
		{
			// Steam tank (left) - output, R-click empty bucket to drain
			new LabelWidgetSpec(X: 6,  Y: 18, Text: "Steam",  Scale: 0.7f),
			new FluidSlotWidgetSpec(X: 6,  Y: 28, Width: 14, Height: 54, Direction: IO.OUT, TankIndex: 0),

			// Water tank - input, R-click water bucket to fill. (Drain blocked
			// by SteamBoilerMachine.GetTankClickCaps - upstream parity.)
			new LabelWidgetSpec(X: 24, Y: 18, Text: "Water", Scale: 0.7f),
			new FluidSlotWidgetSpec(X: 24, Y: 28, Width: 14, Height: 54, Direction: IO.IN, TankIndex: 0),

			// Temperature bar - vertical fill, cold->hot colour ramp. Mirrors
			// upstream's ProgressWidget(getTemperaturePercent, 10x54, DOWN_TO_UP).
			new LabelWidgetSpec(X: 42, Y: 18, Text: "Temp",  Scale: 0.7f),
			new TemperatureBarWidgetSpec(X: 42, Y: 28, Width: 14, Height: 54),

			// Fuel slot (mid) + Ash slot above it + progress arrow between.
			// Bound to the real per-handler groups (NOT SlotGroup.Inventory,
			// which is a read-only concat view - clicks through it void items).
			new LabelWidgetSpec(X: 72, Y: 18, Text: "Ash",   Scale: 0.7f),
			new SlotWidgetSpec(X: 72, Y: 26, Group: TerrariaCompat.Machine.SlotGroup.InventoryOutput, SlotIndex: 0),

			new ProgressArrowWidgetSpec(X: 72, Y: 48, Progress: () => boiler.Progress01),

			new LabelWidgetSpec(X: 72, Y: 64, Text: "Fuel",  Scale: 0.7f),
			new SlotWidgetSpec(X: 72, Y: 72, Group: TerrariaCompat.Machine.SlotGroup.InventoryInput, SlotIndex: 0),

			// Live status - shared with the world tooltip
			new DynamicLabelWidgetSpec(X: 102, Y: 28,
				Getter: () => RecipeStatusText.StatusLine(boiler.Recipe, "Burning"), Scale: 0.65f),

			new DynamicLabelWidgetSpec(X: 102, Y: 50, Getter: () =>
			{
				var water = boiler.GetTank(0);
				var steam = boiler.GetTank(1);
				return $"Steam {(steam.IsEmpty ? 0 : steam.Amount):N0}mB\n" +
				       $"Water {(water.IsEmpty ? 0 : water.Amount):N0}mB";
			}, Scale: 0.6f),
		},
	};
}
