#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

namespace GregTechCEuTerraria.Api.Machine.Feature.Multiblock;

// Port of com.gregtechceu.gtceu.api.machine.feature.multiblock.
// IDisplayUIMachine.
//
// Hook surface for the multi's status-display text. Upstream's
// `addDisplayText` walks every bound part and appends each part's text via
// `IMultiPart.addMultiText`. Multis aggregate energy state, recipe progress,
// part diagnostics etc. into one scrollable panel through this.
//
// Documented adaptations:
//   - `Component` -> `string`. No Mojang text system; multiblock text uses
//     locale keys + parameter substitution at render time.
//   - `createUI(Player)` factory DROPPED. Upstream returns a `ModularUI`
//     built from LDLib widgets (DraggableScrollableWidgetGroup / Label
//     Widget / ComponentPanelWidget). Our UI goes through `MachineUILayout` -
//     the multi controller's `LayoutKey` picks a layout that calls back into
//     `AddDisplayText` to fill the display column.
//   - `handleDisplayClick` kept as the click-callback contract; layout
//     surfaces dispatch into it.
//   - `getScreenTexture` DROPPED - texture spec lives in `MachineDefinition`
//     and the layout, not in this interface.
//   - `self()` cast kept (default body); every implementer is a
//     `MultiblockControllerMachine`.
public interface IDisplayUIMachine
{
	MultiblockControllerMachine Self() => (MultiblockControllerMachine)this;

	// Build the display column's text. Default fans out to every bound part's
	// `AddMultiText`. Multis override to add their own lines (energy state,
	// recipe progress, etc.) before / after the part loop.
	void AddDisplayText(List<string> textList)
	{
		foreach (var part in Self().GetParts())
			part.AddMultiText(textList);
	}

	// Called when the player clicks a text component that carries a click
	// handle (upstream's `ComponentPanelWidget.clickHandler`). Default no-op.
	// `componentData` is the per-component string the multi chose to encode;
	// no LDLib ClickData equivalent - modifier-state would come through the
	// layout's own click pipeline if/when needed.
	void HandleDisplayClick(string componentData) { }
}
