#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Minimal layout for the Data Access Hatch - a square slot grid sized to the
// hatch's data-item inventory (HV 4 = 2x2, EV 9 = 3x3, LuV 16 = 4x4; creative
// has 0 slots -> title only). Mirrors upstream DataAccessHatchMachine.createUIWidget
// (a sqrt(N)xsqrt(N) grid). Only data items are accepted (the handler filter
// enforces it server-side).
public static class DataAccessHatchLayout
{
	private const int SlotSize = 22;
	private const int Padding  = 12;
	private const int TitleH   = 14;

	public static MachineUILayout Build(DataAccessHatchMachine hatch)
	{
		int size = hatch.ImportItems?.SlotCount ?? 0;
		int rows = (int)System.Math.Sqrt(size);
		int cols = rows == 0 ? 0 : (size + rows - 1) / rows;

		int stripW = cols * SlotSize;
		int stripH = rows * SlotSize;

		var layout = new MachineUILayout
		{
			Width  = Padding + System.Math.Max(stripW, 80) + Padding,
			Height = Padding + TitleH + System.Math.Max(stripH, SlotSize) + Padding,
			Title  = hatch.DisplayName,
		};

		for (int idx = 0; idx < size; idx++)
		{
			int c = idx % cols, r = idx / cols;
			layout.Widgets.Add(new SlotWidgetSpec(
				X: Padding + c * SlotSize,
				Y: Padding + TitleH + r * SlotSize,
				Group: TerrariaCompat.Machine.SlotGroup.InventoryInput,
				SlotIndex: idx));
		}

		return layout;
	}
}
