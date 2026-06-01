#nullable enable
using System;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Block Breaker GUI. Adapted from upstream BlockBreakerMachine.createTemplate
// (BlockBreakerMachine.java:275-347). Upstream renders an output cache grid;
// our Terraria port drops the cache (drops fall in-world), so this layout is
// minimal: title bar + energy bar + status readout.
//
// The auto-appended satellites (charger slot top-right, power toggle top-left,
// IO-config cluster above the main panel) supply everything else.
public static class BlockBreakerLayout
{
	public static MachineUILayout Build(BlockBreakerMachine machine)
	{
		const int Padding  = 12;
		const int EnergyW  = 18;
		const int LabelRow = 14;

		// Status block: ~3 lines of small text.
		const int StatusW = 140;
		const int StatusH = 60;

		int contentH = StatusH;
		int width  = Padding + EnergyW + 8 + StatusW + Padding;
		int height = Padding + LabelRow + contentH + Padding;
		int contentTop = Padding + LabelRow;

		var layout = new MachineUILayout
		{
			Width  = width,
			Height = height,
			Title  = machine.DisplayName,
		};

		// Energy bar.
		int leftX = Padding;
		layout.Widgets.Add(new EnergyBarWidgetSpec(
			X: leftX, Y: contentTop, Width: EnergyW, Height: StatusH));

		// Status readout.
		int statusX = leftX + EnergyW + 8;
		int statusY = contentTop;
		layout.Widgets.Add(new LabelWidgetSpec(
			X: statusX, Y: statusY,
			Text: $"Range: {machine.Range} tiles", Scale: 0.7f));
		long euPerTick = VoltageTiers.Voltage((VoltageTier)Math.Max(0, (int)machine.Tier - 1));
		layout.Widgets.Add(new LabelWidgetSpec(
			X: statusX, Y: statusY + 14,
			Text: $"Draw: {euPerTick:N0} EU/t", Scale: 0.7f));
		layout.Widgets.Add(new DynamicLabelWidgetSpec(
			X: statusX, Y: statusY + 28,
			Getter: () => machine.IsActive
				? "Drilling..."
				: (machine.IsWorkingEnabled() ? "Idle" : "Disabled"),
			Scale: 0.7f));

		return layout;
	}
}
