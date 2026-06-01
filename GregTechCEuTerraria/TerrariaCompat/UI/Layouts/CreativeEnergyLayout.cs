#nullable enable
using System;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Net.Actions;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines.Creative;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Layout for the Creative Energy Container - infinite EU source/sink.
public static class CreativeEnergyLayout
{
	public static MachineUILayout Build(CreativeEnergyContainerMachine cec) => new()
	{
		Width = 160,
		Height = 110,
		Title = cec.DisplayName,

		Widgets =
		{
			// Tier-cycle button instead of a raw long voltage stepper - upstream's
			// CreativeEnergyContainerMachine.createUI uses a SelectorWidget that
			// cycles through GTValues.VNF (the tier short names). LMB advances to
			// the next tier, RMB goes back. Display: "LV (32 EU/t)".
			new LabelWidgetSpec(X: 12, Y: 24, Text: "Voltage:", Scale: 0.72f),
			new TextButtonWidgetSpec(
				X: 60, Y: 24,
				Label: () => VoltageLabel(cec.Voltage),
				OnLeft:  () => MachineActions.Send(CreativeEnergySetAction.Voltage(NextTierVoltage(cec.Voltage,  +1)), cec),
				OnRight: () => MachineActions.Send(CreativeEnergySetAction.Voltage(NextTierVoltage(cec.Voltage,  -1)), cec),
				Tooltip: "LMB: higher tier  *  RMB: lower tier",
				Width: 88, Height: 14),
			new NumericStepperWidgetSpec(
				X: 12, Y: 44,
				Label: "amps:",
				Getter: () => cec.Amps,
				Setter: v => MachineActions.Send(CreativeEnergySetAction.Amps((int)v), cec),
				Min: 0, Max: int.MaxValue, Step: 1, LabelWidth: 50),

			// Source / sink toggle.
			new TextButtonWidgetSpec(
				X: 12, Y: 64,
				Label: () => cec.Source ? "[Source]" : "[Sink]",
				OnLeft: () => MachineActions.Send(CreativeEnergySetAction.Source(!cec.Source), cec),
				Tooltip: "Toggle source (push) / sink (accept)",
				Width: 60, Height: 14),
			// Active master switch.
			new TextButtonWidgetSpec(
				X: 80, Y: 64,
				Label: () => cec.Active ? "[ ON]" : "[OFF]",
				OnLeft: () => MachineActions.Send(CreativeEnergySetAction.Active(!cec.Active), cec),
				Tooltip: "Toggle active",
				Width: 60, Height: 14),

			// Average I/O readout.
			new DynamicLabelWidgetSpec(
				X: 12, Y: 86,
				Getter: () => $"Avg I/O: {cec.LastAverageEnergyIOPerTick:N0} EU/t",
				Scale: 0.7f),
		},
	};

	// Tier display - "LV (32 EU/t)", "MAX (2,147,483,648 EU/t)", or "(unset)"
	// when voltage = 0. Resolves the voltage back to a tier via
	// VoltageTiers.FloorTierByVoltage (the inverse of `V[tier]`).
	private static string VoltageLabel(long voltage)
	{
		if (voltage <= 0) return "(unset)";
		int t = VoltageTiers.FloorTierByVoltage(voltage);
		// FloorTierByVoltage can return values up to MAX+1 for over-saturated
		// voltages; clamp to the valid tier range so ShortName doesn't IOR.
		t = Math.Clamp(t, 0, (int)VoltageTier.MAX);
		var tier = (VoltageTier)t;
		long tierV = VoltageTiers.Voltage(tier);
		return tierV == voltage
			? $"{VoltageTiers.ShortName(tier)} ({voltage:N0} EU/t)"
			: $"{VoltageTiers.ShortName(tier)}+ ({voltage:N0} EU/t)";
	}

	// Step the voltage to the next tier's V[t]. `+1` -> up, `-1` -> down. Clamped
	// to [0, MAX].
	private static long NextTierVoltage(long currentVoltage, int dir)
	{
		int currentTier = currentVoltage <= 0
			? -1
			: Math.Clamp(VoltageTiers.FloorTierByVoltage(currentVoltage), 0, (int)VoltageTier.MAX);
		int next = currentTier + dir;
		if (next < 0)                   return 0;             // below ULV -> "(unset)"
		if (next > (int)VoltageTier.MAX) return VoltageTiers.Voltage(VoltageTier.MAX);
		return VoltageTiers.Voltage((VoltageTier)next);
	}
}
