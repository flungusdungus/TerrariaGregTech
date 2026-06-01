#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Primitive;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Coke Oven multiblock GUI: 1 input slot (coal/log) -> progress arrow -> 1
// output slot (coke/charcoal) + creosote fluid tank. No EU (primitive multi).
public static class CokeOvenLayout
{
	public static MachineUILayout Build(CokeOvenMachine m) => new()
	{
		Width  = 180,
		Height = 116,
		Title  = m.DisplayName,

		Widgets =
		{
			// Input item slot (left) - coal / log goes here.
			new LabelWidgetSpec(X: 12, Y: 26, Text: "Input", Scale: 0.75f),
			new SlotWidgetSpec (X: 12, Y: 40, Group: SlotGroup.InventoryInput,  SlotIndex: 0),

			// Progress arrow (centre) - fills as the recipe progresses.
			new ProgressArrowWidgetSpec(X: 50, Y: 44, Progress: () => (float)m.Recipe.GetProgressPercent()),

			// Output item slot (right of arrow) - coke / charcoal.
			new LabelWidgetSpec(X: 84, Y: 26, Text: "Output", Scale: 0.75f),
			new SlotWidgetSpec (X: 84, Y: 40, Group: SlotGroup.InventoryOutput, SlotIndex: 0),

			// Output fluid tank - creosote. R-click empty bucket to drain.
			new LabelWidgetSpec    (X: 116, Y: 26, Text: "Creosote", Scale: 0.7f),
			new FluidSlotWidgetSpec(X: 116, Y: 40, Width: 22, Height: 48, Direction: IO.OUT, TankIndex: 0),

			// Live status under the slots - multi-aware: shows "Structure not
			// formed" until the pattern matches, then falls through to recipe
			// status (working / waiting / idle). Shared shape with the world
			// hover tooltip via MultiblockControllerMachine.AppendTooltip.
			new DynamicLabelWidgetSpec(X: 12, Y: 92,
				Getter: () => RecipeStatusText.StatusLineForMulti(m, m.Recipe), Scale: 0.7f),
		},
	};
}
