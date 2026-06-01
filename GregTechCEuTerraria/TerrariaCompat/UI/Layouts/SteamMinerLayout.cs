#nullable enable
using System;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Steam;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Steam Miner GUI - steam tank | status readout | 2x2 output cache.
// Mirrors MinerLayout, but the left fixture is a fluid widget over the
// steam tank instead of an EnergyBar (steam machines have no EU).
public static class SteamMinerLayout
{
	public static MachineUILayout Build(SteamMinerMachine miner)
	{
		const int InventorySize = 4;
		const int RowSize       = 2;  // sqrt(4) - matches upstream's 2x2 grid

		const int SlotSize = 22;
		const int SlotGap  = 2;
		const int Padding  = 12;
		const int TankW    = 22;
		const int LabelRow = 14;

		int cacheW = RowSize * SlotSize + (RowSize - 1) * SlotGap;
		int cacheH = cacheW;
		const int StatusW = 160;
		int statusH = cacheH;

		int contentH = Math.Max(cacheH, statusH);
		int width    = Padding + TankW + 8 + StatusW + 8 + cacheW + Padding;
		int height   = Padding + LabelRow + contentH + Padding;
		int contentTop = Padding + LabelRow;

		var layout = new MachineUILayout
		{
			Width  = width,
			Height = height,
			Title  = miner.DisplayName,
		};

		// Steam tank.
		int leftX = Padding;
		layout.Widgets.Add(new FluidSlotWidgetSpec(
			X: leftX, Y: contentTop, Width: TankW, Height: cacheH,
			Direction: IO.OUT, TankIndex: 0));

		// Status readout.
		int statusX = leftX + TankW + 8;
		int statusY = contentTop;
		layout.Widgets.Add(new LabelWidgetSpec(
			X: statusX, Y: statusY,
			Text: $"Area: {miner.Width}x{miner.Depth}", Scale: 0.7f));
		layout.Widgets.Add(new LabelWidgetSpec(
			X: statusX, Y: statusY + 14,
			Text: $"Speed: {miner.TicksPerOre}t / ore", Scale: 0.7f));
		layout.Widgets.Add(new LabelWidgetSpec(
			X: statusX, Y: statusY + 28,
			Text: $"Steam: {miner.SteamPerTick} mB/t", Scale: 0.7f));
		layout.Widgets.Add(new DynamicLabelWidgetSpec(
			X: statusX, Y: statusY + 42,
			Getter: () => miner.IsActive
				? "Mining..."
				: (miner.IsWorkingEnabled() ? "Idle" : "Disabled"),
			Scale: 0.7f));

		// Output cache (2x2 grid).
		int cacheX = statusX + StatusW + 8;
		for (int r = 0; r < RowSize; r++)
		{
			for (int c = 0; c < RowSize; c++)
			{
				int idx = r * RowSize + c;
				if (idx >= InventorySize) break;
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
