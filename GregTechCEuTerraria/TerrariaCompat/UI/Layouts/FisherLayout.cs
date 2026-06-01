#nullable enable
using System;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Net.Actions;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Fisher GUI - port of upstream FisherMachine.createTemplate +
// createBatterySlot + createJunkButton (FisherMachine.java:285-391).
//
// Layout shape (left -> right):
//   energy bar | bait slot | junk toggle (under bait) | output cache grid
//
// Output cache grid is (tier+1)^2 slots in a (tier+1) x (tier+1) square
// (LV=2x2, MV=3x3, HV=4x4, EV=5x5, IV=6x6, LuV=7x7). Cache slots are output-
// only (group = InventoryOutput) - the player can take items out but not
// insert.
//
// Charger slot + power toggle + IO-config cluster are auto-appended as
// satellites by MachineUIState (AppendChargerSlot / AppendPowerTogglePanel /
// AppendIOConfigPanel) - NEVER add another `SlotGroup.Charger` here.
public static class FisherLayout
{
	public static MachineUILayout Build(FisherMachine fisher)
	{
		int t = (int)fisher.Tier;
		int rowSize = t + 1;
		int inventorySize = rowSize * rowSize;

		const int SlotSize = 22;
		const int SlotGap  = 2;
		const int Padding  = 12;
		const int EnergyW  = 18;
		const int LabelRow = 14;

		// Left column: energy bar (height matches the cache grid). The charger
		// slot is auto-appended outside the panel by MachineUIState - see header.
		int cacheW = rowSize * SlotSize + (rowSize - 1) * SlotGap;
		int cacheH = cacheW;
		int leftW  = EnergyW;

		// Bait column: bait slot (top) + junk toggle (below).
		int baitW = SlotSize;
		int baitH = SlotSize + 4 + SlotSize;

		int contentH = Math.Max(cacheH, baitH);
		int width  = Padding + leftW + 8 + baitW + 8 + cacheW + Padding;
		int height = Padding + LabelRow + contentH + Padding;

		int contentTop = Padding + LabelRow;

		var layout = new MachineUILayout
		{
			Width  = width,
			Height = height,
			Title  = fisher.DisplayName,
		};

		// Energy bar (left edge), height = cache grid height.
		int leftX = Padding;
		layout.Widgets.Add(new EnergyBarWidgetSpec(
			X: leftX, Y: contentTop, Width: EnergyW, Height: cacheH));

		// Bait column.
		int baitX = leftX + leftW + 8;
		int baitY = contentTop + (cacheH - baitH) / 2;
		layout.Widgets.Add(new SlotWidgetSpec(
			X: baitX, Y: baitY,
			Group: SlotGroup.InventoryInput,
			SlotIndex: 0));

		// Junk toggle - below the bait slot. Upstream uses a NameTag icon;
		// closest analogue in our widget set is `button_blacklist` (the toggle
		// gates whether junk results are included in the loot pool, mirroring
		// the FISHING vs FISHING_FISH split). PNG is a 20x40 vertical strip:
		// top = ON, bottom = OFF.
		layout.Widgets.Add(new ToggleButtonWidgetSpec(
			X: baitX, Y: baitY + SlotSize + 4,
			IconAssetPath: "GregTechCEuTerraria/Content/Textures/gui/widget/button_blacklist",
			Getter: () => fisher.JunkEnabled,
			Setter: v => MachineActions.Send(new JunkToggleAction(v), fisher),
			Tooltip: "Junk loot enabled\nOff: catches fish only (consumes 2 bait per catch)\nOn: full loot pool (consumes 1 bait per catch)")
		{ VerticalSplit = true });

		// Output cache grid.
		int cacheX = baitX + baitW + 8;
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
