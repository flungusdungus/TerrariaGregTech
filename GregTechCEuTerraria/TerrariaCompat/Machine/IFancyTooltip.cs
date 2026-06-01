#nullable enable
using System.Collections.Generic;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Adaptation of IFancyTooltip. Lives in TerrariaCompat (GUI glue + Api can't
// depend on tML). Terraria's hover is a flat text list (no panel / icon
// column), so icon + TooltipComponent dropped. Upstream's RecipeLogic-side
// implementation lives in WorkableTieredMachine / SteamWorkableMachine
// (Api-layer RecipeLogic can't depend on this).
public interface IFancyTooltip
{
	void AppendFancyTooltip(List<string> lines);

	bool ShowFancyTooltip() => true;
}
