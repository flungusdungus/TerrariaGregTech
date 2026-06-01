#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine.Feature;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Layout for input_bus / output_bus - a `sqrt(N) x sqrt(N)` slot grid over the
// part's `(1+min(9,tier))^2` inventory. Mirrors ItemBusPartMachine.createUIWidget
// (ItemBusPartMachine.java:295-322). Decisions:
//   - Output filter slot DROPPED (covers' filter UI is the canonical editor).
//   - Circuit slot wired for IN buses via CircuitSetAction ->
//     IHasCircuitSlot, stored as a real IntCircuitItem in CircuitInventory[0].
// Working-enabled toggle comes from MachineUIState - don't re-add it here.
public static class ItemBusLayout
{
	private const int SlotSize = 22;     // matches UISlot native unscaled size
	private const int Padding  = 12;
	private const int TitleH   = 14;

	public static MachineUILayout Build(ItemBusPartMachine bus)
	{
		int size = bus.Inventory?.SlotCount ?? 0;
		int rows = (int)System.Math.Sqrt(size);
		int cols = rows == 0 ? 0 : (size + rows - 1) / rows;

		int stripW = cols * SlotSize;
		int stripH = rows * SlotSize;

		var group = bus.Io == IO.IN
			? TerrariaCompat.Machine.SlotGroup.InventoryInput
			: TerrariaCompat.Machine.SlotGroup.InventoryOutput;

		// Circuit slot: IN buses only, hidden when IsCircuitSlotEnabled() is off
		// (controller disallows it) to match upstream's "no slot shown".
		bool showCircuit = bus.Io == IO.IN
			&& bus is IHasCircuitSlot icc && icc.IsCircuitSlotEnabled()
			&& bus.CircuitInventory != null && bus.CircuitInventory.SlotCount > 0;
		int circuitColW = showCircuit ? (Padding + SlotSize) : 0;

		var layout = new MachineUILayout
		{
			Width  = Padding + stripW + circuitColW + Padding,
			Height = Padding + TitleH + stripH + Padding,
			Title  = bus.DisplayName,
		};

		for (int idx = 0; idx < size; idx++)
		{
			int c = idx % cols, r = idx / cols;
			layout.Widgets.Add(new SlotWidgetSpec(
				X: Padding + c * SlotSize,
				Y: Padding + TitleH + r * SlotSize,
				Group: group,
				SlotIndex: idx));
		}

		if (showCircuit)
		{
			int circuitX = Padding + stripW + Padding;
			int circuitY = Padding + TitleH + (stripH - SlotSize) / 2; // centred on grid
			layout.Widgets.Add(new CircuitButtonWidgetSpec(X: circuitX, Y: circuitY));
		}

		return layout;
	}
}
