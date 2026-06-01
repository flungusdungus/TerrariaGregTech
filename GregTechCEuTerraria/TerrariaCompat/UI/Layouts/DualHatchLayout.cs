#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Layout for dual_input_hatch / dual_output_hatch - an NxN item-slot grid with
// a vertical column of N fluid-tank widgets to its right, where N = sqrt of the
// part's item inventory. Mirrors upstream DualHatchPartMachine.createUIWidget
// (DualHatchPartMachine.java:156-182). No circuit slot - upstream's override
// doesn't render one (unlike the plain ItemBus / FluidHatch layouts).
//
// Working-enabled toggle is added by MachineUIState as a satellite panel for
// every IControllable machine - do not re-add it here.
public static class DualHatchLayout
{
	private const int SlotSize = 22;
	private const int Padding  = 12;
	private const int TitleH   = 14;

	public static MachineUILayout Build(DualHatchPartMachine hatch)
	{
		int size = hatch.Inventory?.SlotCount ?? 0;
		int n    = (int)System.Math.Sqrt(size);

		int itemStripW = n * SlotSize;
		int stripH     = n * SlotSize;
		int tankColX   = Padding + itemStripW;

		var group = hatch.Io == IO.IN
			? TerrariaCompat.Machine.SlotGroup.InventoryInput
			: TerrariaCompat.Machine.SlotGroup.InventoryOutput;

		var layout = new MachineUILayout
		{
			Width  = Padding + itemStripW + SlotSize + Padding,
			Height = Padding + TitleH + stripH + Padding,
			Title  = hatch.DisplayName,
		};

		int baseY = Padding + TitleH;

		for (int idx = 0; idx < size; idx++)
		{
			int c = idx % n, r = idx / n;
			layout.Widgets.Add(new SlotWidgetSpec(
				X: Padding + c * SlotSize,
				Y: baseY + r * SlotSize,
				Group: group,
				SlotIndex: idx));
		}

		int tankCount = hatch.Tank?.Storages.Length ?? n;
		for (int t = 0; t < tankCount; t++)
		{
			layout.Widgets.Add(new FluidSlotWidgetSpec(
				X: tankColX,
				Y: baseY + t * SlotSize,
				Width: SlotSize, Height: SlotSize,
				Direction: hatch.Io,
				TankIndex: t));
		}

		return layout;
	}
}
