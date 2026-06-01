#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Net.Actions;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines.Creative;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Layout for the Creative Chest - infinite item source debug tool.
// Phantom source slot + two numeric steppers + auto-output toggle.
public static class CreativeChestLayout
{
	public static MachineUILayout Build(CreativeChestTileEntity chest) => new()
	{
		Width = 140,
		Height = 90,
		Title = chest.DisplayName,

		Widgets =
		{
			// Phantom source slot - top-left. Click with cursor item -> set source.
			new LabelWidgetSpec(X: 12, Y: 24, Text: "Source", Scale: 0.8f),
			new CreativeSourceItemSlotWidgetSpec(
				X: 12, Y: 36,
				Getter: () => chest.StoredItem,
				Setter: item => MachineActions.Send(CreativeChestSetAction.SetSourceType(item), chest)),

			// Numeric steppers.
			new NumericStepperWidgetSpec(
				X: 40, Y: 36,
				Label: "items/cycle:",
				Getter: () => chest.ItemsPerCycle,
				Setter: v => MachineActions.Send(CreativeChestSetAction.ItemsPerCycle((int)v), chest),
				Min: 1, Max: int.MaxValue, Step: 1, LabelWidth: 50),
			new NumericStepperWidgetSpec(
				X: 40, Y: 56,
				Label: "ticks/cycle:",
				Getter: () => chest.TicksPerCycle,
				Setter: v => MachineActions.Send(CreativeChestSetAction.TicksPerCycle((int)v), chest),
				Min: 1, Max: int.MaxValue, Step: 1, LabelWidth: 50),

			// Auto-output toggle.
			new ToggleButtonWidgetSpec(
				X: 12, Y: 72,
				IconAssetPath: "GregTechCEuTerraria/Content/Textures/gui/widget/button_item_output_overlay",
				Getter: () => chest.IsAutoOutput,
				Setter: v => MachineActions.Send(new ChestAction(ChestAction.Op.AutoOutput, v), chest),
				Tooltip: "Auto-output the source item to adjacent inventories"),
		},
	};
}
