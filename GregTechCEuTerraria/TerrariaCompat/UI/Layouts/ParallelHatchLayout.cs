#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;
using GregTechCEuTerraria.TerrariaCompat.Net.Actions;
using GregTechCEuTerraria.TerrariaCompat.UI.Widgets;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Layout for `parallel_hatch`. Adapted from upstream's `IntInputWidget`
// (ParallelHatchPartMachine.java:51-56) - Terraria has no native int-input
// widget, so we surface the value as stepper buttons (-1 / +1 + min / max
// shortcuts) plus a live readout. Every click ships through ParallelSetAction
// for server-authority + clamping in SetCurrentParallel.
public static class ParallelHatchLayout
{
	private const int Padding = 12;
	private const int TitleH  = 14;
	private const int BtnW    = 26;
	private const int BtnH    = 18;
	private const int Gap     = 4;

	public static MachineUILayout Build(ParallelHatchPartMachine hatch)
	{
		const int rowW = 4 * BtnW + 3 * Gap;
		var layout = new MachineUILayout
		{
			Width  = Padding + rowW + Padding,
			Height = Padding + TitleH + 6 + 18 /* readout */ + 6 + BtnH + Padding,
			Title  = hatch.DisplayName,
		};

		int y = Padding + TitleH + 6;

		layout.Widgets.Add(new DynamicLabelWidgetSpec(
			X: Padding, Y: y,
			Getter: () => $"Parallel: {hatch.CurrentParallel} / {hatch.MaxParallel}",
			Scale: 0.85f));

		y += 18 + 6;
		int x = Padding;
		layout.Widgets.Add(new ButtonSpec(x, y, "MIN",
			() => MachineActions.Send(new ParallelSetAction(ParallelHatchPartMachine.MIN_PARALLEL), hatch),
			Tooltip: "Set parallel count to 1"));
		x += BtnW + Gap;
		layout.Widgets.Add(new ButtonSpec(x, y, "-1",
			() => MachineActions.Send(new ParallelSetAction(hatch.CurrentParallel - 1), hatch),
			Tooltip: "Decrease parallel count by 1"));
		x += BtnW + Gap;
		layout.Widgets.Add(new ButtonSpec(x, y, "+1",
			() => MachineActions.Send(new ParallelSetAction(hatch.CurrentParallel + 1), hatch),
			Tooltip: "Increase parallel count by 1"));
		x += BtnW + Gap;
		layout.Widgets.Add(new ButtonSpec(x, y, "MAX",
			() => MachineActions.Send(new ParallelSetAction(hatch.MaxParallel), hatch),
			Tooltip: $"Set parallel count to max ({hatch.MaxParallel})"));

		return layout;
	}

	// Inline widget spec - a text button that fires its callback on left click.
	// Kept private here because no other layout needs a stepper-style button.
	private sealed record ButtonSpec(int X, int Y, string Label, System.Action OnClick, string Tooltip)
		: WidgetSpec(X, Y)
	{
		public override UIElement Create(MetaMachine entity)
			=> new UITextButton(() => Label, onLeft: OnClick, tooltip: Tooltip, width: BtnW, height: BtnH);
	}
}
