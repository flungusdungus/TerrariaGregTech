#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Net.Actions;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Layout factory for the Drum - a single fluid slot plus the auto-output
// toggle. Geometry trimmed from SuperTankLayout: a drum is the plain storage
// tier, so it has no lock / void toggle.
public static class DrumLayout
{
	public static MachineUILayout Build(DrumMachine drum) => new()
	{
		Width = 140,
		Height = 96,
		Title = drum.DisplayName,

		Widgets =
		{
			// Text readout - top-left.
			new LabelWidgetSpec(X: 12, Y: 28, Text: "Fluid Amount", Scale: 0.8f),
			new DynamicLabelWidgetSpec(X: 12, Y: 42, Getter: () =>
				$"{drum.GetTank(0).Amount:N0} / {drum.Capacity:N0} mB"),
			new DynamicLabelWidgetSpec(X: 12, Y: 56, Getter: () =>
				drum.GetTank(0).Type?.DisplayName ?? "(no fluid)"),

			// Fluid slot - interactive (R-click buckets / fluid cells). Single
			// bidirectional tank, the drum itself (GetTankAccess(0) => this).
			new FluidSlotWidgetSpec(X: 100, Y: 26, Width: 18, Height: 60,
				Direction: IO.BOTH, TankIndex: 0),

			// Auto-output toggle - bottom-left.
			new ToggleButtonWidgetSpec(
				X: 12, Y: 72,
				IconAssetPath: "GregTechCEuTerraria/Content/Textures/gui/widget/button_fluid_output_overlay",
				Getter: () => drum.IsAutoOutput,
				Setter: v => MachineActions.Send(
					new TankConfigSetAction(TankConfigSetAction.Field.AutoOutput, v), drum),
				Tooltip: "Auto-output to adjacent containers"),
		},
	};
}
