#nullable enable
using System;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Miner GUI - energy bar | status readout | (tier+1)^2 output cache grid.
// Charger slot + power toggle + IO-config cluster are auto-appended as
// satellites by MachineUIState. Shape modeled after FisherLayout (same
// (tier+1)^2 cache convention).
public static class MinerLayout
{
	public static MachineUILayout Build(MinerMachine miner)
	{
		int t = (int)miner.Tier;
		int rowSize = t + 1;
		int inventorySize = rowSize * rowSize;

		const int SlotSize = 22;
		const int SlotGap  = 2;
		const int Padding  = 12;
		const int EnergyW  = 18;
		const int LabelRow = 14;

		int cacheW = rowSize * SlotSize + (rowSize - 1) * SlotGap;
		int cacheH = cacheW;
		const int StatusW = 160;
		int statusH = cacheH;

		int contentH = Math.Max(cacheH, statusH);
		int width  = Padding + EnergyW + 8 + StatusW + 8 + cacheW + Padding;
		int height = Padding + LabelRow + contentH + Padding;
		int contentTop = Padding + LabelRow;

		var layout = new MachineUILayout
		{
			Width  = width,
			Height = height,
			Title  = miner.DisplayName,
		};

		// Energy bar.
		int leftX = Padding;
		layout.Widgets.Add(new EnergyBarWidgetSpec(
			X: leftX, Y: contentTop, Width: EnergyW, Height: cacheH));

		// Status readout.
		int statusX = leftX + EnergyW + 8;
		int statusY = contentTop;
		layout.Widgets.Add(new LabelWidgetSpec(
			X: statusX, Y: statusY,
			Text: $"Area: {miner.Width}x{miner.Depth}", Scale: 0.7f));
		long euPerTick = VoltageTiers.Voltage((VoltageTier)Math.Max(0, (int)miner.Tier - 1));
		layout.Widgets.Add(new LabelWidgetSpec(
			X: statusX, Y: statusY + 14,
			Text: $"Draw: {euPerTick:N0} EU/t", Scale: 0.7f));
		layout.Widgets.Add(new DynamicLabelWidgetSpec(
			X: statusX, Y: statusY + 28,
			Getter: () => miner.IsActive
				? "Mining..."
				: (miner.IsWorkingEnabled() ? "Idle" : "Disabled"),
			Scale: 0.7f));

		// Output cache grid.
		int cacheX = statusX + StatusW + 8;
		for (int r = 0; r < rowSize; r++)
		{
			for (int c = 0; c < rowSize; c++)
			{
				int idx = r * rowSize + c;
				if (idx >= inventorySize) break;
				layout.Widgets.Add(new SlotWidgetSpec(
					X: cacheX + c * (SlotSize + SlotGap),
					Y: contentTop + r * (SlotSize + SlotGap),
					Group: SlotGroup.InventoryOutput,
					SlotIndex: idx));
			}
		}

		return layout;
	}
}
