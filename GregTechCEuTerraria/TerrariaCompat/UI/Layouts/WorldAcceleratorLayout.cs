#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// World Accelerator GUI. Adapted from upstream WorldAcceleratorMachine - but
// the machine has no inventory, no charger slot, no mode toggle in our port
// (BlockEntity mode dropped). Layout shape mirrors BlockBreakerLayout:
// title + energy bar + 3 status labels.
//
// Power toggle + IO-config cluster are auto-appended as satellites by
// MachineUIState (AppendPowerTogglePanel / AppendIOConfigPanel). No charger
// slot to render - upstream WorldAccelerator doesn't register one.
public static class WorldAcceleratorLayout
{
	public static MachineUILayout Build(WorldAcceleratorMachine machine)
	{
		const int Padding  = 12;
		const int EnergyW  = 18;
		const int LabelRow = 14;

		const int StatusW = 160;
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
			Text: $"Area: {machine.AreaSide}x{machine.AreaSide} tiles", Scale: 0.7f));
		long euPerTick = 3L * Common.Energy.VoltageTiers.Voltage(machine.Tier);
		layout.Widgets.Add(new LabelWidgetSpec(
			X: statusX, Y: statusY + 14,
			Text: $"Draw: {euPerTick:N0} EU/t", Scale: 0.7f));
		layout.Widgets.Add(new DynamicLabelWidgetSpec(
			X: statusX, Y: statusY + 28,
			Getter: () => machine.IsActive
				? "Accelerating"
				: (machine.IsWorkingEnabled() ? "Idle" : "Disabled"),
			Scale: 0.7f));

		return layout;
	}
}
