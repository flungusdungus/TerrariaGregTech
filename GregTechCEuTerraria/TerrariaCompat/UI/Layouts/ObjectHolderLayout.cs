#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Minimal layout for the Object Holder - two slots: slot 0 (the item to
// research) and slot 1 (the data orb). Mirrors upstream ObjectHolderMachine.
// createUIWidget's two BlockableSlotWidgets (the in-GUI "blocked while locked"
// state is enforced server-side by the handler's extract gate).
public static class ObjectHolderLayout
{
	private const int SlotSize = 22;
	private const int Padding  = 12;
	private const int TitleH   = 14;
	private const int Gap      = 30;

	public static MachineUILayout Build(ObjectHolderMachine holder)
	{
		var layout = new MachineUILayout
		{
			Width  = Padding + SlotSize + Gap + SlotSize + Padding,
			Height = Padding + TitleH + SlotSize + Padding,
			Title  = holder.DisplayName,
		};
		int y = Padding + TitleH;
		// Block both slots while the controller has locked the holder mid-recipe
		// (port of upstream's BlockableSlotWidget.setIsBlocked(this::isLocked)).
		// Server-side SlotAction re-checks the same lock so the gate holds in MP.
		System.Func<bool> blocked = () => holder.IsLocked;
		// slot 0 = research subject
		layout.Widgets.Add(new SlotWidgetSpec(
			X: Padding, Y: y,
			Group: TerrariaCompat.Machine.SlotGroup.InventoryInput, SlotIndex: 0,
			IsBlocked: blocked));
		// slot 1 = data orb
		layout.Widgets.Add(new SlotWidgetSpec(
			X: Padding + SlotSize + Gap, Y: y,
			Group: TerrariaCompat.Machine.SlotGroup.InventoryInput, SlotIndex: 1,
			IsBlocked: blocked));
		return layout;
	}
}
