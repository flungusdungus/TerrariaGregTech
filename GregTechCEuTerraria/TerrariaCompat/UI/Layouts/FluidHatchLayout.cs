#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine.Feature;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Mirrors FluidHatchPartMachine.createUIWidget (java:268-370). 1x = tall tank
// + readout; 4x/9x = sqrtN grid. Circuit slot for IN hatches. Working-enabled
// toggle is the universal MachineUIState satellite - don't re-add here.
// Output-side phantom-fluid lock widget dropped (covers' phantoms are item-only).
public static class FluidHatchLayout
{
	private const int SlotSize = 22;
	private const int Padding  = 12;
	private const int TitleH   = 14;

	public static MachineUILayout Build(FluidHatchPartMachine hatch)
	{
		int slots = hatch.Tank?.Storages.Length ?? 1;
		bool showCircuit = hatch.Io == IO.IN
			&& hatch is IHasCircuitSlot icc && icc.IsCircuitSlotEnabled()
			&& hatch.CircuitInventory != null && hatch.CircuitInventory.SlotCount > 0;
		return slots == 1 ? BuildSingle(hatch, showCircuit) : BuildGrid(hatch, slots, showCircuit);
	}

	// Returns extra width consumed (slot + padding) or 0. Caller bakes this
	// into the layout Width at construction (Width is init-only).
	private static int EmitCircuitSlot(MachineUILayout layout, FluidHatchPartMachine hatch, bool show, int contentRightX, int contentBaseY, int contentHeight)
	{
		if (!show) return 0;
		int circuitX = contentRightX + Padding;
		int circuitY = contentBaseY + System.Math.Max(0, (contentHeight - SlotSize) / 2);
		layout.Widgets.Add(new CircuitButtonWidgetSpec(X: circuitX, Y: circuitY));
		return Padding + SlotSize;
	}

	private static MachineUILayout BuildSingle(FluidHatchPartMachine hatch, bool showCircuit)
	{
		const int TankW = 18;
		const int TankH = 60;
		const int ReadoutW = 110;

		int contentW = ReadoutW + 6 + TankW;
		int contentH = TankH;
		int circuitW = showCircuit ? (Padding + SlotSize) : 0;

		var layout = new MachineUILayout
		{
			Width  = Padding + contentW + circuitW + Padding,
			Height = Padding + TitleH + contentH + Padding,
			Title  = hatch.DisplayName,
		};

		int readoutX = Padding;
		int tankX    = Padding + ReadoutW + 6;
		int baseY    = Padding + TitleH;

		layout.Widgets.Add(new LabelWidgetSpec(X: readoutX, Y: baseY + 2,  Text: "Fluid Amount", Scale: 0.8f));
		layout.Widgets.Add(new DynamicLabelWidgetSpec(X: readoutX, Y: baseY + 16, Getter: () =>
		{
			var t = hatch.Tank?.Storages[0];
			if (t is null) return "0 / 0 mB";
			return $"{t.Fluid.Amount:N0} / {t.Capacity:N0} mB";
		}));
		layout.Widgets.Add(new DynamicLabelWidgetSpec(X: readoutX, Y: baseY + 30, Getter: () =>
			hatch.Tank?.Storages[0].Fluid.Type?.DisplayName ?? "(no fluid)"));

		layout.Widgets.Add(new FluidSlotWidgetSpec(X: tankX, Y: baseY,
			Width: TankW, Height: TankH,
			Direction: hatch.Io, TankIndex: 0));

		EmitCircuitSlot(layout, hatch,
			show: showCircuit,
			contentRightX: tankX + TankW,
			contentBaseY: baseY,
			contentHeight: contentH);

		return layout;
	}

	// Upstream createMultiSlotGUI (java:346-370). Each tank = Storages[i].
	private static MachineUILayout BuildGrid(FluidHatchPartMachine hatch, int slots, bool showCircuit)
	{
		int rows = (int)System.Math.Sqrt(slots);
		int cols = rows == 0 ? 0 : (slots + rows - 1) / rows;

		int stripW = cols * SlotSize;
		int stripH = rows * SlotSize;
		int circuitW = showCircuit ? (Padding + SlotSize) : 0;

		var layout = new MachineUILayout
		{
			Width  = Padding + stripW + circuitW + Padding,
			Height = Padding + TitleH + stripH + Padding,
			Title  = hatch.DisplayName,
		};

		int baseY = Padding + TitleH;

		for (int idx = 0; idx < slots; idx++)
		{
			int c = idx % cols, r = idx / cols;
			layout.Widgets.Add(new FluidSlotWidgetSpec(
				X: Padding + c * SlotSize,
				Y: baseY + r * SlotSize,
				Width: SlotSize, Height: SlotSize,
				Direction: hatch.Io,
				TankIndex: idx));
		}

		EmitCircuitSlot(layout, hatch,
			show: showCircuit,
			contentRightX: Padding + stripW,
			contentBaseY: baseY,
			contentHeight: stripH);

		return layout;
	}
}
