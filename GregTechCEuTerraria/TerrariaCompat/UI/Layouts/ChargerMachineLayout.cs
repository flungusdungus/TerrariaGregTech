#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Charger / Battery Buffer GUI - N-slot battery grid + energy bar. Grid shape
// mirrors BatteryBufferMachine.createUIWidget: 4->2x2, 8->4x2, 16->4x4, 64->8x8.
// Shared by ChargerMachine + BatteryBufferMachine (only title + slot count differ).
public static class ChargerMachineLayout
{
	public static MachineUILayout BuildGeneric(int slotCount, string title)
	{
		var (cols, rows) = GridShapeFor(slotCount);
		const int SlotSize = 22;
		const int SlotGap  = 2;
		const int Padding  = 12;
		int slotStripW = cols * SlotSize + (cols - 1) * SlotGap;
		int slotStripH = rows * SlotSize + (rows - 1) * SlotGap;
		const int EnergyBarW = 18;
		const int EnergyBarH = 60;
		int contentH = System.Math.Max(slotStripH, EnergyBarH);
		int width  = Padding + slotStripW + 8 + EnergyBarW + Padding;
		int height = Padding + contentH + Padding + 14; // +14 for title

		var layout = new MachineUILayout
		{
			Width  = width,
			Height = height,
			Title  = title,
		};

		for (int r = 0; r < rows; r++)
		{
			for (int c = 0; c < cols; c++)
			{
				int idx = r * cols + c;
				if (idx >= slotCount) break;
				int x = Padding + c * (SlotSize + SlotGap);
				int y = Padding + 14 + r * (SlotSize + SlotGap);
				layout.Widgets.Add(new SlotWidgetSpec(
					X: x, Y: y,
					Group: SlotGroup.Inventory,
					SlotIndex: idx));
			}
		}

		layout.Widgets.Add(new EnergyBarWidgetSpec(
			X: Padding + slotStripW + 8,
			Y: Padding + 14,
			Width: EnergyBarW,
			Height: contentH));

		return layout;
	}

	// Mirror upstream's `int rowSize = (int) Math.sqrt(inventorySize); int
	// colSize = rowSize; if (inventorySize == 8) { rowSize = 4; colSize = 2; }`
	private static (int cols, int rows) GridShapeFor(int n) => n switch
	{
		4  => (2, 2),
		8  => (4, 2),
		16 => (4, 4),
		64 => (8, 8),
		_  => ((int)System.Math.Sqrt(n), (int)System.Math.Sqrt(n)),
	};
}
