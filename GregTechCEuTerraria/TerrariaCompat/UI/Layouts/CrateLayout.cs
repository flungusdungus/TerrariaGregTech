#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Crate inventory GUI - the N-slot item grid. Row width mirrors upstream
// CrateMachine.createUI: 9 slots per row for crates under 90 slots, 18 per row
// for the larger ones. (Large grids are safe - UISlot draws every slot through
// vanilla ItemSlot.Draw at index 0, so the chest-sized nav/context tables never
// overflow.)
public static class CrateLayout
{
	public static MachineUILayout Build(CrateMachine crate)
	{
		int size = crate.InventorySize;
		int cols = size >= 90 ? 18 : 9;
		int rows = (size + cols - 1) / cols;

		const int SlotSize = 22;
		const int SlotGap  = 2;
		const int Padding  = 12;
		const int TitleH   = 14;

		int stripW = cols * SlotSize + (cols - 1) * SlotGap;
		int stripH = rows * SlotSize + (rows - 1) * SlotGap;

		var layout = new MachineUILayout
		{
			Width  = Padding + stripW + Padding,
			Height = Padding + TitleH + stripH + Padding,
			Title  = crate.DisplayName,
		};

		for (int idx = 0; idx < size; idx++)
		{
			int c = idx % cols, r = idx / cols;
			layout.Widgets.Add(new SlotWidgetSpec(
				X: Padding + c * (SlotSize + SlotGap),
				Y: Padding + TitleH + r * (SlotSize + SlotGap),
				Group: SlotGroup.Inventory,
				SlotIndex: idx));
		}

		return layout;
	}
}
