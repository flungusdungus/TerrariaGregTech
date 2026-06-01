#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Layout for `maintenance_hatch` and `configurable_maintenance_hatch`.
// Shared layout key - the configurable variant adds nothing extra here today
// (the duration-multiplier +/- buttons are deferred until a TextButton-bound
// action lands; the current value already shows in the hover tooltip).
//
// One duct-tape slot (the hatch's `ItemStackHandler` exposed via
// `SlotGroup.Inventory`) + a dynamic problems / taped readout. Everything
// else (cover panel, IO config - n/a here since the hatch isn't a
// TieredIOPart) the abstract MachineUIState attaches around the panel.
public static class MaintenanceHatchLayout
{
	private const int SlotSize = 22;
	private const int Padding  = 12;
	private const int TitleH   = 14;

	public static MachineUILayout Build(MaintenanceHatchPartMachine hatch)
	{
		const int ReadoutW = 160;
		int contentW = ReadoutW + 8 + SlotSize;
		int contentH = SlotSize + 16;   // slot + a row of readout lines

		var layout = new MachineUILayout
		{
			Width  = Padding + contentW + Padding,
			Height = Padding + TitleH + contentH + Padding,
			Title  = hatch.DisplayName,
		};

		int baseY = Padding + TitleH;

		// Live readout - problems count, taped state, optional duration mult.
		// GetNumMaintenanceProblems gates on MaintenanceConfig.Enabled, so when
		// maintenance is globally disabled the readout reads "No problems"
		// regardless of the underlying problem byte (which the hatch keeps
		// populated for the day the config flips back on).
		layout.Widgets.Add(new DynamicLabelWidgetSpec(X: Padding, Y: baseY, Getter: () =>
		{
			int missing = ((Api.Machine.Feature.Multiblock.IMaintenanceMachine)hatch).GetNumMaintenanceProblems();
			return missing == 0 ? "No problems" : $"Problems: {missing} / 6";
		}, Scale: 0.85f));
		layout.Widgets.Add(new DynamicLabelWidgetSpec(X: Padding, Y: baseY + 14, Getter: () =>
			hatch.IsTaped() ? "Taped" : "", Scale: 0.7f));
		if (hatch.IsConfigurable)
		{
			layout.Widgets.Add(new DynamicLabelWidgetSpec(X: Padding, Y: baseY + 28, Getter: () =>
				$"Duration x{hatch.GetDurationMultiplier():F2}", Scale: 0.7f));
		}

		// Duct-tape slot - right side. The handler's filter only admits
		// gtceu:duct_tape, so the slot rejects everything else.
		layout.Widgets.Add(new SlotWidgetSpec(
			X: Padding + ReadoutW + 8,
			Y: baseY,
			Group: SlotGroup.Inventory,
			SlotIndex: 0));

		return layout;
	}
}
