#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Layout factory for the Super Tank. Returns a fresh layout each open so
// dynamic-label / toggle-button widgets can close over the specific entity
// instance.
//
// Geometry adapted from upstream's QuantumTank UI (90x63 sub-group inside
// the standard MC inventory panel). For our Terraria port we drop the
// player-inventory half (Terraria draws its own) and pad the machine area
// to a comfortable 140x96.
public static class SuperTankLayout
{
	public static MachineUILayout Build(SuperTankTileEntity tank) => new()
	{
		Width = 140,
		Height = 96,
		Title = tank.DisplayName,

		Widgets =
		{
			// Text readout - top-left. StoredAmount / MaxAmount are long, so a
			// high-tier tank's full count shows without int clamping.
			new LabelWidgetSpec(X: 12, Y: 28, Text: "Fluid Amount", Scale: 0.8f),
			new DynamicLabelWidgetSpec(X: 12, Y: 42, Getter: () =>
				tank.StoredType is null ? "0 mB" : $"{tank.StoredAmount:N0} / {tank.MaxAmount:N0} mB"),
			new DynamicLabelWidgetSpec(X: 12, Y: 56, Getter: () =>
				tank.StoredType?.DisplayName ?? "(no fluid)"),

			// Fluid slot - right side, full panel height minus padding.
			// Interactive (R-click buckets / fluid cells) - same UIFluidSlot
			// widget every other machine uses. Single bidirectional tank.
			new FluidSlotWidgetSpec(X: 100, Y: 26, Width: 18, Height: 60,
				Direction: IO.BOTH, TankIndex: 0),

			// Three toggles along the bottom - matches upstream layout
			new ToggleButtonWidgetSpec(
				X: 12, Y: 72,
				IconAssetPath: "GregTechCEuTerraria/Content/Textures/gui/widget/button_lock",
				Getter: () => tank.IsLocked,
				Setter: v => TerrariaCompat.Net.Actions.MachineActions.Send(new TerrariaCompat.Net.Actions.TankConfigSetAction(TerrariaCompat.Net.Actions.TankConfigSetAction.Field.Locked, v), tank),
				Tooltip: "Lock to current fluid type"),

			new ToggleButtonWidgetSpec(
				X: 32, Y: 72,
				IconAssetPath: "GregTechCEuTerraria/Content/Textures/gui/widget/button_void_partial",
				Getter: () => tank.IsVoiding,
				Setter: v => TerrariaCompat.Net.Actions.MachineActions.Send(new TerrariaCompat.Net.Actions.TankConfigSetAction(TerrariaCompat.Net.Actions.TankConfigSetAction.Field.Voiding, v), tank),
				Tooltip: "Void overflow (accept then discard)"),

			new ToggleButtonWidgetSpec(
				X: 52, Y: 72,
				IconAssetPath: "GregTechCEuTerraria/Content/Textures/gui/widget/button_fluid_output_overlay",
				Getter: () => tank.IsAutoOutput,
				Setter: v => TerrariaCompat.Net.Actions.MachineActions.Send(new TerrariaCompat.Net.Actions.TankConfigSetAction(TerrariaCompat.Net.Actions.TankConfigSetAction.Field.AutoOutput, v), tank),
				Tooltip: "Auto-output to adjacent containers (pipes-less: pushes into immediately-adjacent IFluidHandlers every 5 ticks)"),
		},
	};
}
