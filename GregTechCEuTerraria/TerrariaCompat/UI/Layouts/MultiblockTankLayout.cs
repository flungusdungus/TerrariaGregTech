#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Layout factory for the multiblock tank controllers (wooden / bronze /
// steel). Mirrors upstream's `MultiblockTankMachine.createUIWidget` - a label
// pair (fluid amount / fluid name) plus a single bidirectional fluid slot.
// No lock / void / auto-output toggles: upstream's tank UI doesn't carry any
// (auto-output lives on the bound tank valve, not the controller).
public static class MultiblockTankLayout
{
	public static MachineUILayout Build(MultiblockTankMachine tank) => new()
	{
		Width = 140,
		Height = 96,
		Title = tank.DisplayName,

		Widgets =
		{
			new LabelWidgetSpec(X: 12, Y: 28, Text: "Fluid Amount", Scale: 0.8f),
			new DynamicLabelWidgetSpec(X: 12, Y: 42, Getter: () =>
				$"{tank.GetTank(0).Amount:N0} / {tank.Capacity:N0} mB"),
			new DynamicLabelWidgetSpec(X: 12, Y: 56, Getter: () =>
				tank.GetTank(0).Type?.DisplayName ?? "(no fluid)"),

			new FluidSlotWidgetSpec(X: 100, Y: 26, Width: 18, Height: 60,
				Direction: IO.BOTH, TankIndex: 0),
		},
	};
}
