#nullable enable
using System;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Steam;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Layout for SimpleSteamMachine - the steam-powered processing machines.
//
// Mirrors GenericMachineLayout's `input | arrow | output` grid, but the
// right-side fixture is the steam tank (a fluid widget) instead of an EU
// energy bar: a steam machine is powered by steam, not EU. No overclock /
// EU-per-tick labels (steam machines don't voltage-overclock).
public static class SteamMachineLayout
{
	private const int SlotStride = 22;     // matches UISlot.NativeUnscaledSize
	private const int GroupPad   = 4;
	private const int MaxCols    = 3;
	private const int ArrowSize  = 22;
	private const int ArrowGap   = 40;

	public static MachineUILayout Build(SimpleSteamMachine m, string title)
	{
		int inCount  = m.InputSlots;
		int outCount = m.OutputSlots;

		var (inW,  inH)  = MeasureItems(inCount);
		var (outW, outH) = MeasureItems(outCount);
		int maxGroupW = Math.Max(inW, outW);

		int circuitColumnHeight = m.UsesCircuit ? SlotStride + 4 + ArrowSize : ArrowSize;
		int templateH = Math.Max(Math.Max(inH, outH), circuitColumnHeight);
		int templateW = 2 * maxGroupW + ArrowGap;

		int inputsBaseX  = (maxGroupW - inW) / 2;
		int outputsBaseX = maxGroupW + ArrowGap + (maxGroupW - outW) / 2;
		int arrowX       = maxGroupW + (ArrowGap - ArrowSize) / 2;
		int arrowY       = 40 + (templateH + circuitColumnHeight) / 2 - ArrowSize;

		int leftPad = 12;
		int steamX  = templateW + 6;
		int steamW  = 22;
		int steamH  = Math.Max(SlotStride * 2, templateH - 4);
		int totalW  = leftPad + templateW + 6 + steamW + 12;
		int footerY = 40 + templateH + 10;
		int totalH  = footerY + 22;

		var layout = new MachineUILayout { Width = totalW, Height = totalH, Title = title };

		EmitItemGrid(layout, inCount,  leftPad + inputsBaseX,  40 + (templateH - inH)  / 2, isOutput: false, "Input");
		EmitItemGrid(layout, outCount, leftPad + outputsBaseX, 40 + (templateH - outH) / 2, isOutput: true,  "Output");

		// Progress arrow.
		layout.Widgets.Add(new ProgressArrowWidgetSpec(X: leftPad + arrowX, Y: arrowY, Progress: () => m.Progress01));

		// Steam machines have no programmed-circuit slot upstream - no widget here.

		// Steam tank - the steam machine's "power gauge" (IO.IN, single tank).
		layout.Widgets.Add(new FluidSlotWidgetSpec(X: leftPad + steamX, Y: 40,
			Width: steamW, Height: steamH, Direction: IO.IN, TankIndex: 0));

		// Footer status line.
		layout.Widgets.Add(new DynamicLabelWidgetSpec(X: leftPad, Y: footerY,
			Getter: () => RecipeStatusText.StatusLine(m.Recipe, "Running"), Scale: 0.7f));

		return layout;
	}

	// Item grid measurement - cols capped at 3, rows wrap.
	private static (int W, int H) MeasureItems(int count)
	{
		if (count <= 0) return (0, 0);
		int cols = Math.Min(count, MaxCols);
		int rows = (count + MaxCols - 1) / MaxCols;
		return (cols * SlotStride + 2 * GroupPad, rows * SlotStride + 2 * GroupPad);
	}

	private static void EmitItemGrid(MachineUILayout layout, int count,
		int baseX, int baseY, bool isOutput, string sectionLabel)
	{
		if (count <= 0) return;
		layout.Widgets.Add(new LabelWidgetSpec(X: baseX + GroupPad, Y: baseY - 14, Text: sectionLabel, Scale: 0.7f));

		var group = isOutput ? SlotGroup.InventoryOutput : SlotGroup.InventoryInput;
		for (int s = 0; s < count; s++)
		{
			int col = s % MaxCols;
			int row = s / MaxCols;
			layout.Widgets.Add(new SlotWidgetSpec(
				X: baseX + GroupPad + col * SlotStride,
				Y: baseY + GroupPad + row * SlotStride,
				Group: group, SlotIndex: s));
		}
	}
}
