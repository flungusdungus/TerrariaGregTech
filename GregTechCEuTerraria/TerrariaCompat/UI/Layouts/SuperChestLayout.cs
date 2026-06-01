#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Layout factory for the Super Chest - item-storage mirror of SuperTankLayout.
// Adapted from upstream's QuantumChest UI: a single in-GUI deposit/extract
// slot (LMB-with-cursor -> Insert, LMB-empty -> Dump one stack), an item-count
// readout, plus the lock / void / auto-output toggles. The previous tile-RMB-
// with-held-item insert path was removed (vanilla SyncEquipment ignore-self
// dupe - see SuperChestTile.RightClick).
public static class SuperChestLayout
{
	public static MachineUILayout Build(SuperChestTileEntity chest) => new()
	{
		Width = 140,
		Height = 96,
		Title = chest.DisplayName,

		Widgets =
		{
			// Text readout - top-left.
			new LabelWidgetSpec(X: 12, Y: 28, Text: "Items Stored", Scale: 0.8f),
			new DynamicLabelWidgetSpec(X: 12, Y: 42, Getter: () =>
			{
				var s = chest.StoredItem;
				return s.IsAir ? "0" : $"{chest.StoredAmount:N0} / {chest.MaxAmount:N0}";
			}),
			new DynamicLabelWidgetSpec(X: 12, Y: 56, Getter: () =>
			{
				var s = chest.StoredItem;
				return s.IsAir ? "(empty)" : s.Name;
			}),

			// Single in-GUI deposit/extract slot - top-right. LMB with cursor
			// inserts the cursor stack (leftover returns to cursor); LMB with
			// empty cursor extracts one Item.maxStack into the player's
			// inventory (same op the Dump button uses).
			new SuperChestSlotWidgetSpec(X: 100, Y: 28),

			// Dump button - hands one stack to the player. Kept alongside the
			// slot widget so the player has a one-click extract without having
			// to clear their cursor first.
			new ToggleButtonWidgetSpec(
				X: 100, Y: 54,
				IconAssetPath: "GregTechCEuTerraria/Content/Textures/gui/widget/button_output",
				Getter: () => false,
				Setter: _ => TerrariaCompat.Net.Actions.MachineActions.Send(new TerrariaCompat.Net.Actions.ChestAction(TerrariaCompat.Net.Actions.ChestAction.Op.Dump, true), chest),
				Tooltip: "Take a stack of the stored item"),

			// Three toggles along the bottom - matches the Super Tank layout.
			new ToggleButtonWidgetSpec(
				X: 12, Y: 72,
				IconAssetPath: "GregTechCEuTerraria/Content/Textures/gui/widget/button_lock",
				Getter: () => chest.IsLocked,
				Setter: v => TerrariaCompat.Net.Actions.MachineActions.Send(new TerrariaCompat.Net.Actions.ChestAction(TerrariaCompat.Net.Actions.ChestAction.Op.Locked, v), chest),
				Tooltip: "Lock to current item type"),

			new ToggleButtonWidgetSpec(
				X: 32, Y: 72,
				IconAssetPath: "GregTechCEuTerraria/Content/Textures/gui/widget/button_void_partial",
				Getter: () => chest.IsVoiding,
				Setter: v => TerrariaCompat.Net.Actions.MachineActions.Send(new TerrariaCompat.Net.Actions.ChestAction(TerrariaCompat.Net.Actions.ChestAction.Op.Voiding, v), chest),
				Tooltip: "Void overflow (accept then discard)"),

			new ToggleButtonWidgetSpec(
				X: 52, Y: 72,
				IconAssetPath: "GregTechCEuTerraria/Content/Textures/gui/widget/button_item_output_overlay",
				Getter: () => chest.IsAutoOutput,
				Setter: v => TerrariaCompat.Net.Actions.MachineActions.Send(new TerrariaCompat.Net.Actions.ChestAction(TerrariaCompat.Net.Actions.ChestAction.Op.AutoOutput, v), chest),
				Tooltip: "Auto-output to adjacent inventories (pipes-less: pushes into immediately-adjacent IItemHandlers every 5 ticks)"),
		},
	};
}
