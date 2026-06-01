#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Net.Actions;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines.Creative;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Layout for the Creative Tank - infinite fluid source.
public static class CreativeTankLayout
{
	public static MachineUILayout Build(CreativeTankTileEntity tank) => new()
	{
		Width = 140,
		Height = 90,
		Title = tank.DisplayName,

		Widgets =
		{
			new LabelWidgetSpec(X: 12, Y: 24, Text: "Source", Scale: 0.8f),
			new CreativeSourceFluidSlotWidgetSpec(
				X: 12, Y: 36,
				Getter: () => tank.StoredType,
				Setter: type => MachineActions.Send(CreativeTankSetAction.SetSourceFluid(type), tank)),

			new NumericStepperWidgetSpec(
				X: 40, Y: 36,
				Label: "mB/cycle:",
				Getter: () => tank.MBPerCycle,
				Setter: v => MachineActions.Send(CreativeTankSetAction.MBPerCycle((int)v), tank),
				Min: 1, Max: int.MaxValue, Step: 100, LabelWidth: 50),
			new NumericStepperWidgetSpec(
				X: 40, Y: 56,
				Label: "ticks/cycle:",
				Getter: () => tank.TicksPerCycle,
				Setter: v => MachineActions.Send(CreativeTankSetAction.TicksPerCycle((int)v), tank),
				Min: 1, Max: int.MaxValue, Step: 1, LabelWidth: 50),

			new ToggleButtonWidgetSpec(
				X: 12, Y: 72,
				IconAssetPath: "GregTechCEuTerraria/Content/Textures/gui/widget/button_item_output_overlay",
				Getter: () => tank.IsAutoOutput,
				Setter: v => MachineActions.Send(new TankConfigSetAction(TankConfigSetAction.Field.AutoOutput, v), tank),
				Tooltip: "Auto-output the source fluid to adjacent fluid handlers"),
		},
	};
}
