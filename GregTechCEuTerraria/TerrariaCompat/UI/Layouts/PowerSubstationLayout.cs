#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Electric;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Port of upstream PowerSubstationMachine.createUIWidget (PowerSubstationMachine
// .java:331) - a DraggableScrollableWidgetGroup wrapping a
// ComponentPanelWidget(this::addDisplayText). Here that collapses to one
// MultiLineDynamicLabel driven by the controller's BuildPanelLines, the same
// single-panel shape GenericMultiblockLayout uses.
//
// PowerSubstation extends WorkableMultiblockMachine (NOT WEMM), so it can't use
// the "generic_multi" layout whose getter casts to WorkableElectricMultiblock
// Machine - hence this dedicated (but tiny) layout.
public static class PowerSubstationLayout
{
	private const int Padding = 12;
	private const int TitleH  = 14;
	private const int BodyW   = 280;
	private const int BodyH   = 14 * 12; // up to ~12 lines

	public static MachineUILayout Build(PowerSubstationMachine machine)
	{
		var layout = new MachineUILayout
		{
			Width  = Padding + BodyW + Padding,
			Height = Padding + TitleH + BodyH + Padding,
			Title  = machine.DisplayName,
		};

		layout.Widgets.Add(new MultiLineDynamicLabelWidgetSpec(
			X: Padding, Y: Padding + TitleH,
			Getter: () => machine.BuildPanelLines()));

		return layout;
	}
}
