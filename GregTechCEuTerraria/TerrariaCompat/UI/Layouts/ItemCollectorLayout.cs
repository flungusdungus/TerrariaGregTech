#nullable enable
using System;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Net.Actions;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;
using GregTechCEuTerraria.TerrariaCompat.UI.Widgets;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Port of ItemCollectorMachine.createTemplate + createEnergyBar (java:313-407).
// Layout: [energy bar][filter editor][output grid]. Charger / power-toggle /
// IO-config attached by MachineUIState. Filter editor swaps subtree on
// FilterOrdinal change via SwappableRegionWidgetSpec.
public static class ItemCollectorLayout
{
	private const int SlotSize = 22;
	private const int SlotGap  = 2;
	private const int Padding  = 12;
	private const int EnergyW  = 18;
	private const int LabelRow = 14;
	private const int PhantomGrid = 3 * SlotSize;
	private const int ToggleW = 96, ToggleH = 16;
	private const int HeaderH = 16;

	public static MachineUILayout Build(ItemCollectorMachine machine)
	{
		// rowSize = sqrtinventorySize: LV(9)=3x3, MV(16)=4x4, HV/EV(25)=5x5.
		int inventorySize = machine.Output.Storage.SlotCount;
		int rowSize = (int)Math.Round(Math.Sqrt(inventorySize));

		int outW = rowSize * SlotSize + (rowSize - 1) * SlotGap;
		int outH = outW;

		int filterW = PhantomGrid + 6 + ToggleW;
		int filterRegionH = PhantomGrid + 4 + 12;

		int contentH = Math.Max(outH, HeaderH + 4 + filterRegionH);
		int width  = Padding + EnergyW + 8 + filterW + 8 + outW + Padding;
		int height = Padding + LabelRow + contentH + Padding;

		int contentTop = Padding + LabelRow;

		var layout = new MachineUILayout
		{
			Width  = width,
			Height = height,
			Title  = machine.DisplayName,
		};

		int leftX = Padding;
		layout.Widgets.Add(new EnergyBarWidgetSpec(
			X: leftX, Y: contentTop, Width: EnergyW, Height: outH));

		int filterX = leftX + EnergyW + 8;
		int filterY = contentTop;

		// Filter-type cycle - stays put across editor rebuilds.
		layout.Widgets.Add(new TextButtonWidgetSpec(
			X: filterX, Y: filterY,
			Label:   () => "Filter: " + (machine.FilterOrdinal == 1 ? "Tags" : "Items"),
			OnLeft:  () => MachineActions.Send(MachineFilterAction.Cycle(), machine),
			OnRight: () => MachineActions.Send(MachineFilterAction.Cycle(), machine),
			Tooltip: "Items - 3x3 phantom item grid\nTags - tag expression",
			Width: PhantomGrid, Height: HeaderH));

		// Swaps subtree on ordinal flip.
		int editorY = filterY + HeaderH + 4;
		layout.Widgets.Add(new SwappableRegionWidgetSpec(
			X: filterX, Y: editorY,
			Width: filterW, Height: filterRegionH,
			Signature: e => ((IFilterableMachine)e).FilterOrdinal,
			Build: BuildFilterEditor));

		int outX = filterX + filterW + 8;
		for (int r = 0; r < rowSize; r++)
		{
			for (int c = 0; c < rowSize; c++)
			{
				int idx = r * rowSize + c;
				if (idx >= inventorySize) break;
				layout.Widgets.Add(new SlotWidgetSpec(
					X: outX + c * (SlotSize + SlotGap),
					Y: contentTop + r * (SlotSize + SlotGap),
					Group: SlotGroup.InventoryOutput,
					SlotIndex: idx));
			}
		}

		return layout;
	}

	// Children positioned in already-scaled pixels (`s` = layout scale).
	private static void BuildFilterEditor(UISwappableContainer container, float s, MetaMachine entity)
	{
		var machine = (ItemCollectorMachine)entity;

		if (machine.FilterOrdinal == 1)
		{
			var field = new UITextField(
				current:     () => machine.TagFilter.OreDictFilterExpression ?? "",
				onConfirm:   txt => MachineActions.Send(MachineFilterAction.TagExpr(txt ?? ""), machine),
				maxLength:   64,
				placeholder: "tag expression  *  e.g.  *dusts/gold | !*lv",
				tooltip:     TagFilterInfo)
			{
				Left   = StyleDimension.FromPixels(0),
				Top    = StyleDimension.FromPixels(0),
				Width  = StyleDimension.FromPixels((PhantomGrid + 6 + ToggleW) * s),
				Height = StyleDimension.FromPixels(18 * s),
			};
			container.Append(field);
		}
		else
		{
			for (int i = 0; i < 9; i++)
			{
				int gx = (i % 3) * SlotSize;
				int gy = (i / 3) * SlotSize;
				var slot = new UIMachineFilterPhantomSlot(machine, () => machine.SimpleFilter, i)
				{
					Left   = StyleDimension.FromPixels(gx * s),
					Top    = StyleDimension.FromPixels(gy * s),
					Width  = StyleDimension.FromPixels(SlotSize * s - 1),   // 1-px seam
					Height = StyleDimension.FromPixels(SlotSize * s - 1),
				};
				container.Append(slot);
			}

			int tx = PhantomGrid + 6;
			var blacklistBtn = new UITextButton(
				label:   () => machine.SimpleFilter.IsBlackList ? "Mode: Blacklist" : "Mode: Whitelist",
				onLeft:  () => MachineActions.Send(MachineFilterAction.Toggle(MachineFilterAction.Op.ToggleBlacklist), machine),
				onRight: () => MachineActions.Send(MachineFilterAction.Toggle(MachineFilterAction.Op.ToggleBlacklist), machine),
				tooltip: "Whitelist - only listed items are pulled\nBlacklist - listed items are NOT pulled\n(an empty whitelist pulls nothing)",
				width:   (int)(ToggleW * s),
				height:  (int)(ToggleH * s))
			{
				Left = StyleDimension.FromPixels(tx * s),
				Top  = StyleDimension.FromPixels(0),
			};
			container.Append(blacklistBtn);

			var nbtBtn = new UITextButton(
				label:   () => "Ignore NBT: " + (machine.SimpleFilter.IgnoreNbt ? "Yes" : "No"),
				onLeft:  () => MachineActions.Send(MachineFilterAction.Toggle(MachineFilterAction.Op.ToggleIgnoreNbt), machine),
				onRight: () => MachineActions.Send(MachineFilterAction.Toggle(MachineFilterAction.Op.ToggleIgnoreNbt), machine),
				tooltip: "Ignore item NBT data when matching",
				width:   (int)(ToggleW * s),
				height:  (int)(ToggleH * s))
			{
				Left = StyleDimension.FromPixels(tx * s),
				Top  = StyleDimension.FromPixels((ToggleH + 4) * s),
			};
			container.Append(nbtBtn);
		}

		// Range readout - always present (mode-independent).
		var range = new UIDynamicLabel(
			getter: () =>
			{
				int t = (int)machine.Tier;
				long draw = t >= 1 ? 6L * (1L << (t - 1)) : 0;
				return $"Range: {machine.Range} / {machine.MaxRange} tiles  (draw {draw} EU/t)";
			},
			scale: 0.6f)
		{
			Left = StyleDimension.FromPixels(0),
			Top  = StyleDimension.FromPixels((PhantomGrid + 4) * s),
		};
		container.Append(range);
	}

	// Verbatim from CoverSettingsUI / MagnetUIState.
	private const string TagFilterInfo =
		"Accepts complex expressions:\n"
		+ "a & b = AND   *   a | b = OR   *   a ^ b = XOR\n"
		+ "!a = NOT   *   (a) for grouping\n"
		+ "* = wildcard   *   $ = untagged\n"
		+ "Tags are 'namespace:tag/subtype'.\n"
		+ "The 'forge:' namespace is assumed if one isn't given.\n"
		+ "Example: *dusts/gold | (gtceu:circuits & !*lv)\n"
		+ "Type, then press Enter (or click away) to set.";
}
