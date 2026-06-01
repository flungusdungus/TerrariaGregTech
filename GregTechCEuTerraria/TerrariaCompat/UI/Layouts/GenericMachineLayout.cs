#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// 1:1 port of upstream GTRecipeTypeUI.addInventorySlotGroup +
// createEditableUITemplate (GTRecipeTypeUI.java:200-217). Per side, walks
// (capability, count) pairs ordered by RecipeCapability.COMPARATOR (items then
// fluids); slots at (idx%3, idx/3), max 3 cols, row break between capabilities.
// Template = `2 x maxWidth + arrowGap`.
//
// Deviations: slot stride = 22 (vanilla-inventory alignment) instead of 18;
// energy bar + circuit selector added as side fixtures; status + EU readout
// below. Fluid slots are 22x22 (uniform-grid parity, no taller tanks).
public static class GenericMachineLayout
{
	private const int SlotStride = 22;     // matches UISlot.NativeUnscaledSize
	private const int GroupPad   = 4;      // upstream pad
	private const int MaxCols    = 3;      // upstream cap
	private const int ArrowSize  = 22;     // our progress arrow box size
	private const int ArrowGap   = 40;     // upstream's `2 * maxWidth + 40`

	public static MachineUILayout Build(WorkableTieredMachine m, string title)
	{
		// Items then fluids - we hardcode the upstream COMPARATOR ordering.
		var inputEntries  = BuildEntries(itemCount: m.InputSlots,  fluidCount: m.InputFluidTanks);
		var outputEntries = BuildEntries(itemCount: m.OutputSlots, fluidCount: m.OutputFluidTanks);

		var (inW, inH)   = MeasureGroup(inputEntries);
		var (outW, outH) = MeasureGroup(outputEntries);

		int maxGroupW = System.Math.Max(inW, outW);
		int groupH    = System.Math.Max(inH, outH);

		// Outer template (upstream lines 200-217). Circuit (if present) stacks
		// above the arrow; templateH grows so side groups don't clip.
		int circuitColumnHeight = m.UsesCircuit ? SlotStride + 4 + ArrowSize : ArrowSize;
		int templateW = 2 * maxGroupW + ArrowGap;
		int templateH = System.Math.Max(groupH, circuitColumnHeight);

		int inputsBaseX  = (maxGroupW - inW) / 2;
		int inputsBaseY  = 40 + (templateH - inH) / 2;
		int outputsBaseX = maxGroupW + ArrowGap + (maxGroupW - outW) / 2;
		int outputsBaseY = 40 + (templateH - outH) / 2;
		int arrowX       = maxGroupW + (ArrowGap - ArrowSize) / 2;
		// Arrow at the bottom of its column so the circuit sits above cleanly.
		// No-circuit case still centres (circuitColumnHeight == ArrowSize).
		int arrowY       = 40 + (templateH + circuitColumnHeight) / 2 - ArrowSize;

		int circuitX = arrowX + (ArrowSize - SlotStride) / 2;
		int circuitY = arrowY - SlotStride - 4;
		int energyX  = templateW + 6;
		int energyW  = 18;
		int energyH  = System.Math.Max(SlotStride * 2, templateH - 4);

		int leftPad   = 12;
		int rightPad  = 12;
		int totalW    = leftPad + templateW + 6 + energyW + rightPad;
		// OC + EU/t labels stack under the arrow.
		int ocLabelY  = arrowY + ArrowSize + 2;
		int euLabelY  = ocLabelY + 10;
		int footerY   = System.Math.Max(40 + templateH + 6, euLabelY + 10);
		int totalH    = footerY + 22;

		var layout = new MachineUILayout
		{
			Width  = totalW,
			Height = totalH,
			Title  = title,
		};

		EmitGroup(layout, m, inputEntries,
			baseX: leftPad + inputsBaseX,
			baseY: inputsBaseY,
			slotStartIndex: 0,
			fluidTankStartIndex: 0,
			isOutput: false,
			sectionLabel: "Input");

		// Output indices restart at 0 - widgets carry the IN/OUT direction,
		// the machine resolves the actual handler index. No hand-split here.
		EmitGroup(layout, m, outputEntries,
			baseX: leftPad + outputsBaseX,
			baseY: outputsBaseY,
			slotStartIndex: 0,
			fluidTankStartIndex: 0,
			isOutput: true,
			sectionLabel: "Output");

		layout.Widgets.Add(new ProgressArrowWidgetSpec(X: leftPad + arrowX, Y: arrowY, Progress: () => m.Progress01));
		layout.Widgets.Add(new DynamicLabelWidgetSpec(X: leftPad + arrowX, Y: ocLabelY,
			Getter: () => m.ActiveOverclock > 0 ? $"OCx{m.ActiveOverclock}" : "",
			Scale: 0.65f));
		layout.Widgets.Add(new DynamicLabelWidgetSpec(X: leftPad + arrowX, Y: euLabelY,
			Getter: () => m.IsRunning ? $"{m.ActiveEuPerTick} EU/t" : "",
			Scale: 0.65f));

		if (m.UsesCircuit)
		{
			layout.Widgets.Add(new CircuitButtonWidgetSpec(X: leftPad + circuitX, Y: circuitY));
		}

		layout.Widgets.Add(new EnergyBarWidgetSpec(X: leftPad + energyX, Y: 40, Width: energyW, Height: energyH));

		// Footer: status + energy readout.
		layout.Widgets.Add(new DynamicLabelWidgetSpec(X: leftPad, Y: footerY,
			Getter: () => RecipeStatusText.StatusLine(m.Recipe), Scale: 0.7f));
		layout.Widgets.Add(new DynamicLabelWidgetSpec(X: leftPad + energyX - 4, Y: footerY, Getter: () =>
			$"{m.EnergyStored:N0} EU", Scale: 0.55f));

		return layout;
	}

	private readonly record struct Entry(int Count, bool IsFluid);

	private static List<Entry> BuildEntries(int itemCount, int fluidCount)
	{
		var list = new List<Entry>();
		if (itemCount  > 0) list.Add(new Entry(itemCount,  IsFluid: false));
		if (fluidCount > 0) list.Add(new Entry(fluidCount, IsFluid: true));
		return list;
	}

	// Mirror of upstream addInventorySlotGroup measurement (lines 290-317):
	//   maxCount x stride + 2*pad  wide,  totalRows x stride + 2*pad  tall.
	private static (int W, int H) MeasureGroup(List<Entry> entries)
	{
		int maxCount = 0;
		int totalRows = 0;
		foreach (var e in entries)
		{
			if (e.Count > maxCount) maxCount = System.Math.Min(e.Count, MaxCols);
			totalRows += (e.Count + MaxCols - 1) / MaxCols;
		}
		if (maxCount == 0) return (0, 0);
		return (maxCount * SlotStride + 2 * GroupPad,
		        totalRows * SlotStride + 2 * GroupPad);
	}

	// Mirror of upstream addInventorySlotGroup emission (lines 318-337):
	// walk slots at (idx%3, idx/3); after each capability pad idx to next row.
	private static void EmitGroup(MachineUILayout layout, WorkableTieredMachine m,
		List<Entry> entries, int baseX, int baseY,
		int slotStartIndex, int fluidTankStartIndex, bool isOutput, string sectionLabel)
	{
		if (entries.Count == 0) return;

		layout.Widgets.Add(new LabelWidgetSpec(X: baseX + GroupPad, Y: baseY - 14, Text: sectionLabel, Scale: 0.7f));

		int index = 0;
		int itemSlotCursor = slotStartIndex;
		int fluidTankCursor = fluidTankStartIndex;
		foreach (var e in entries)
		{
			for (int s = 0; s < e.Count; s++)
			{
				int col = index % MaxCols;
				int row = index / MaxCols;
				int x = baseX + GroupPad + col * SlotStride;
				int y = baseY + GroupPad + row * SlotStride;
				if (e.IsFluid)
				{
					layout.Widgets.Add(new FluidSlotWidgetSpec(X: x, Y: y,
						Width: SlotStride, Height: SlotStride,
						Direction: isOutput ? IO.OUT : IO.IN,
						TankIndex: fluidTankCursor++));
				}
				else
				{
					var group = isOutput
						? TerrariaCompat.Machine.SlotGroup.InventoryOutput
						: TerrariaCompat.Machine.SlotGroup.InventoryInput;
					layout.Widgets.Add(new SlotWidgetSpec(X: x, Y: y,
						Group: group, SlotIndex: itemSlotCursor++));
				}
				index++;
			}
			// Pad to next row so capabilities don't share a partial row.
			index += (MaxCols - (index % MaxCols)) % MaxCols;
		}
	}
}
