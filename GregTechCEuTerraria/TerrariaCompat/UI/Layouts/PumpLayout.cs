#nullable enable
using System;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Pump GUI - energy bar | status readout | two tank columns (water + lava).
// Charger slot + power toggle + IO-config cluster are auto-appended as
// satellites by MachineUIState. Tank widgets use IO.OUT so player can drain
// into a bucket but not pour back in (the pump produces, not consumes).
public static class PumpLayout
{
	public static MachineUILayout Build(PumpMachine pump)
	{
		const int Padding  = 12;
		const int EnergyW  = 18;
		const int LabelRow = 14;
		const int TankW    = 22;
		const int TankH    = 60;
		const int TankGap  = 6;
		const int StatusW  = 160;
		int statusH = TankH;

		int tanksW   = TankW * 2 + TankGap;
		int contentH = Math.Max(TankH, statusH);
		int width    = Padding + EnergyW + 8 + StatusW + 8 + tanksW + Padding;
		int height   = Padding + LabelRow + contentH + Padding;
		int contentTop = Padding + LabelRow;

		var layout = new MachineUILayout
		{
			Width  = width,
			Height = height,
			Title  = pump.DisplayName,
		};

		// Energy bar.
		int leftX = Padding;
		layout.Widgets.Add(new EnergyBarWidgetSpec(
			X: leftX, Y: contentTop, Width: EnergyW, Height: TankH));

		// Status readout.
		int statusX = leftX + EnergyW + 8;
		int statusY = contentTop;
		layout.Widgets.Add(new LabelWidgetSpec(
			X: statusX, Y: statusY,
			Text: $"Area: {pump.Width}x{pump.Depth}", Scale: 0.7f));
		long euPerTick = VoltageTiers.Voltage((VoltageTier)Math.Max(0, (int)pump.Tier - 1));
		layout.Widgets.Add(new LabelWidgetSpec(
			X: statusX, Y: statusY + 14,
			Text: $"Draw: {euPerTick:N0} EU/t", Scale: 0.7f));
		layout.Widgets.Add(new DynamicLabelWidgetSpec(
			X: statusX, Y: statusY + 28,
			Getter: () => pump.IsActive
				? "Pumping..."
				: (pump.IsWorkingEnabled() ? "Idle" : "Disabled"),
			Scale: 0.7f));

		// Two tanks: water (index 0), lava (index 1).
		int tanksX = statusX + StatusW + 8;
		layout.Widgets.Add(new FluidSlotWidgetSpec(
			X: tanksX, Y: contentTop, Width: TankW, Height: TankH,
			Direction: IO.OUT, TankIndex: 0));
		layout.Widgets.Add(new FluidSlotWidgetSpec(
			X: tanksX + TankW + TankGap, Y: contentTop, Width: TankW, Height: TankH,
			Direction: IO.OUT, TankIndex: 1));

		return layout;
	}
}
