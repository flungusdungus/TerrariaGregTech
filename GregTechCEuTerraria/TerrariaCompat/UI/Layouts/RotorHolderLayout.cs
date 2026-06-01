#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Layout for `rotor_holder` - port of RotorHolderPartMachine.createUIWidget
// (RotorHolderPartMachine.java:233-242): one BlockableSlotWidget, SLOT +
// TURBINE_OVERLAY background, isBlocked while rotorSpeed != 0 (can't pull a
// spinning rotor).
public static class RotorHolderLayout
{
	private const int SlotSize = 22;
	private const int Padding  = 12;
	private const int TitleH   = 14;

	public static MachineUILayout Build(RotorHolderPartMachine holder)
	{
		var layout = new MachineUILayout
		{
			Width  = Padding + SlotSize + Padding,
			Height = Padding + TitleH + SlotSize + Padding + 16, // +16 for a status line
			Title  = holder.DisplayName,
		};

		int slotX = Padding;
		int slotY = Padding + TitleH;

		// IsBlocked = setIsBlocked(() -> rotorSpeed != 0); EmptyOverlayAsset =
		// the TURBINE_OVERLAY background shown on an empty slot.
		layout.Widgets.Add(new SlotWidgetSpec(
			X: slotX, Y: slotY,
			Group: SlotGroup.RotorSlot,
			SlotIndex: 0,
			IsBlocked: () => holder.RotorSpeed != 0,
			EmptyOverlayAsset: "GregTechCEuTerraria/Content/Textures/gui/overlay/turbine_overlay"));

		// Live speed / efficiency / power readout.
		layout.Widgets.Add(new DynamicLabelWidgetSpec(
			X: slotX + SlotSize + 6,
			Y: slotY,
			Getter: () =>
			{
				if (!holder.HasRotor()) return "No rotor";
				int eff = holder.GetRotorEfficiency();
				int pow = holder.GetRotorPower();
				string speed = $"{holder.RotorSpeed}/{holder.MaxRotorHolderSpeed} RPM";
				return $"{speed}\nEff {eff}%  Pow {pow}";
			},
			Scale: 0.7f));

		return layout;
	}
}
