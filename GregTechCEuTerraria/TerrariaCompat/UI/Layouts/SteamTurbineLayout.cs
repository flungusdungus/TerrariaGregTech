#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.UI.Widgets;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Shared GUI for the recipe-driven generators (steam turbine, gas turbine,
// combustion) - input fuel tank -> progress arrow -> EU buffer bar. An output
// fluid tank is shown only when the definition declares one: the steam turbine
// emits distilled water, but the gas / combustion turbines have no fluid output
// (OutputFluidTankCount == 0), so binding an IO.OUT slot to tank 0 there would
// reference a nonexistent tank.
public static class SteamTurbineLayout
{
	public static MachineUILayout Build(SimpleGeneratorMachine m)
	{
		bool hasOutput = (m.Definition?.OutputFluidTankCount ?? 0) > 0;

		var layout = new MachineUILayout
		{
			Width  = 180,
			Height = 116,
			Title  = m.DisplayName,

			Widgets =
			{
				new LabelWidgetSpec(X: 12, Y: 26, Text: "Fuel In", Scale: 0.7f),
				new FluidSlotWidgetSpec(X: 12, Y: 40, Width: 22, Height: 22, Direction: IO.IN, TankIndex: 0),

				new ProgressArrowWidgetSpec(X: 50, Y: 44, Progress: () => m.Progress01),

				new EnergyBarWidgetSpec(X: 142, Y: 40, Width: 18, Height: 48),

				new DynamicLabelWidgetSpec(X: 12, Y: 92,
					Getter: () => RecipeStatusText.StatusLine(m.Recipe), Scale: 0.7f),
				new DynamicLabelWidgetSpec(X: 138, Y: 92, Getter: () =>
					$"{m.EnergyStored:N0} EU", Scale: 0.6f),
			},
		};

		if (hasOutput)
		{
			layout.Widgets.Add(new LabelWidgetSpec(X: 84, Y: 26, Text: "Fluid Out", Scale: 0.65f));
			layout.Widgets.Add(new FluidSlotWidgetSpec(X: 84, Y: 40, Width: 22, Height: 22, Direction: IO.OUT, TankIndex: 0));
		}

		return layout;
	}
}
