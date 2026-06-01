#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Primitive;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Primitive Blast Furnace multiblock GUI: 3 input slots (iron ingot / fuel /
// igniter) -> progress arrow -> 3 output slots (steel ingot / dust / dust). No
// fluid tank, no EU (primitive multi). Mirror of upstream
// PrimitiveBlastFurnaceMachine.createUI (GTMultiMachines / PBFM.java:131-157)
// - vertical column of 3 input slots on the left, 3 horizontal output slots
// on the right, progress arrow between them.
public static class PrimitiveBlastFurnaceLayout
{
	public static MachineUILayout Build(PrimitiveBlastFurnaceMachine m) => new()
	{
		Width  = 184,
		Height = 116,
		Title  = m.DisplayName,

		Widgets =
		{
			// Input column (left) - 3 slots stacked vertically. Upstream's
			// background overlays are INGOT / DUST / FURNACE - we don't
			// surface the per-slot icon hint, but the slot order matches
			// upstream so recipes / muscle memory both map cleanly.
			new LabelWidgetSpec(X: 12, Y: 26, Text: "Input", Scale: 0.75f),
			new SlotWidgetSpec (X: 12, Y: 40, Group: SlotGroup.InventoryInput, SlotIndex: 0),
			new SlotWidgetSpec (X: 12, Y: 62, Group: SlotGroup.InventoryInput, SlotIndex: 1),
			new SlotWidgetSpec (X: 12, Y: 84, Group: SlotGroup.InventoryInput, SlotIndex: 2),

			// Progress arrow (centre).
			new ProgressArrowWidgetSpec(X: 56, Y: 62, Progress: () => (float)m.Recipe.GetProgressPercent()),

			// Output row (right) - 3 horizontal slots, matching upstream's
			// (104,38), (122,38), (140,38) layout.
			new LabelWidgetSpec(X: 96, Y: 26, Text: "Output", Scale: 0.75f),
			new SlotWidgetSpec (X: 96,  Y: 40, Group: SlotGroup.InventoryOutput, SlotIndex: 0),
			new SlotWidgetSpec (X: 118, Y: 40, Group: SlotGroup.InventoryOutput, SlotIndex: 1),
			new SlotWidgetSpec (X: 140, Y: 40, Group: SlotGroup.InventoryOutput, SlotIndex: 2),

			// Live status under the slots - multi-aware: shows "Structure not
			// formed" until the pattern matches, then falls through to recipe
			// status (working / waiting / idle). Shared shape with the world
			// hover tooltip via MultiblockControllerMachine.AppendTooltip.
			new DynamicLabelWidgetSpec(X: 12, Y: 100,
				Getter: () => RecipeStatusText.StatusLineForMulti(m, m.Recipe), Scale: 0.7f),
		},
	};
}
