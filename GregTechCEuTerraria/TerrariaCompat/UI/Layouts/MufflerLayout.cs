#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Layout for the muffler_hatch - recovery-items inventory grid. Each tier
// owns a `(tier+1)^2` slot inventory: 4 at LV, 9 at MV, ... up to 81 at UV.
// Square grid in upstream style (no UI complexity beyond the slots; upstream
// MufflerPartMachine doesn't override `createUI` for any tier).
//
// The slots route through `SlotGroup.Inventory` - the muffler holds a single
// undirectional `CustomItemStackHandler` (not the per-IO split that
// ItemBusPartMachine has), so the legacy combined group works without an
// IN/OUT lookup.
public static class MufflerLayout
{
	private const int SlotSize = 22;
	private const int Padding  = 12;
	private const int TitleH   = 14;

	public static MachineUILayout Build(MufflerPartMachine muffler)
	{
		int size = muffler.Inventory?.SlotCount ?? 0;
		int rows = (int)System.Math.Sqrt(size);
		int cols = rows == 0 ? 0 : (size + rows - 1) / rows;

		int stripW = cols * SlotSize;
		int stripH = rows * SlotSize;

		var layout = new MachineUILayout
		{
			Width  = Padding + stripW + Padding,
			Height = Padding + TitleH + stripH + Padding,
			Title  = muffler.DisplayName,
		};

		for (int idx = 0; idx < size; idx++)
		{
			int c = idx % cols, r = idx / cols;
			layout.Widgets.Add(new SlotWidgetSpec(
				X: Padding + c * SlotSize,
				Y: Padding + TitleH + r * SlotSize,
				Group: SlotGroup.Inventory,
				SlotIndex: idx));
		}

		return layout;
	}
}
