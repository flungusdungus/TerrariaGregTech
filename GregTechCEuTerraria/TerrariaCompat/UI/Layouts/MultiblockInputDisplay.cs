#nullable enable
using System.Collections.Generic;
using System.Text;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// TerrariaCompat display aid (no upstream parallel): when a multi is formed but
// not running, list the actual contents of its input buses / fluid hatches under
// a "Current Inputs:" header. Lets the player see what's loaded vs what a recipe
// needs - especially for ORDERED multis (Assembly Line), where the per-bus order
// is what makes or breaks the match, so each bus/hatch is numbered left-to-right
// (GetParts() is X-sorted via the controller's GetPartSorter).
public static class MultiblockInputDisplay
{
	public static void Append(MultiblockControllerMachine controller, List<string> lines)
	{
		var rows = new List<string>();
		int busIndex = 0, hatchIndex = 0;

		// MUST mirror the recipe logic's input-handler filter EXACTLY so the bus /
		// hatch NUMBERING here matches what the matcher uses - both walk GetParts()
		// (X-sorted) and both gate on Io==IN + ShouldSearchContent (see
		// AssemblyLineMachine.RebuildBusOrdering and upstream's
		// `IRecipeHandler::shouldSearchContent` filter). Without the
		// ShouldSearchContent gate the display could number a bus the logic skips,
		// making "Bus 2" on screen the logic's "Bus 1" - a misleading second source
		// of truth for an ORDERED multi. The contents themselves are read live from
		// the same handler the matcher consumes (bus.Inventory / hatch.Tank).
		foreach (var part in controller.GetParts())
		{
			if (part is ItemBusPartMachine bus && bus.Io == IO.IN
				&& bus.Inventory is { ShouldSearchContent: true } inv)
			{
				busIndex++;
				rows.Add($"  [c/AAAAAA:Bus {busIndex}:] {ItemSummary(inv)}");
			}
			else if (part is FluidHatchPartMachine hatch && hatch.Io == IO.IN
				&& hatch.Tank is { ShouldSearchContent: true } tank)
			{
				hatchIndex++;
				rows.Add($"  [c/AAAAAA:Hatch {hatchIndex}:] {FluidSummary(tank)}");
			}
		}

		if (rows.Count == 0) return;   // no input parts (e.g. generator multi) - nothing to show
		lines.Add("[c/55FFFF:Current Inputs:]");
		lines.AddRange(rows);
	}

	private static string ItemSummary(Api.Machine.Trait.NotifiableItemStackHandler inv)
	{
		var sb = new StringBuilder();
		for (int s = 0; s < inv.GetSlots(); s++)
		{
			var stack = inv.Storage.GetStackInSlot(s);
			if (stack == null || stack.IsAir) continue;
			if (sb.Length > 0) sb.Append(", ");
			sb.Append($"{stack.stack}x {stack.Name}");
		}
		return sb.Length == 0 ? "[c/777777:empty]" : sb.ToString();
	}

	private static string FluidSummary(Api.Machine.Trait.NotifiableFluidTank tank)
	{
		var sb = new StringBuilder();
		for (int t = 0; t < tank.GetTanks(); t++)
		{
			var f = tank.GetFluidInTank(t);
			if (f.IsEmpty) continue;
			if (sb.Length > 0) sb.Append(", ");
			sb.Append($"{f.Amount} mB {f.Type!.DisplayName}");
		}
		return sb.Length == 0 ? "[c/777777:empty]" : sb.ToString();
	}
}
