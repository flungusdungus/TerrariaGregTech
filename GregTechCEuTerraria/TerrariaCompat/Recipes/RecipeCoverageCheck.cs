#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Recipes;

// Load-time sanity check: every recipe-driven machine's GTRecipeType should
// resolve to a non-empty recipe station.
//
// A registry-name mismatch (e.g. "combustion" vs upstream's "combustion_
// generator") otherwise fails SILENTLY - the machine just shows an empty
// browser. This surfaces those as a load-time log line.
//
// Run AFTER MachineRegistry and RecipeRegistry are populated.
public static class RecipeCoverageCheck
{
	public static void Verify(Mod mod)
	{
		// Distinct recipe-type station ids -> the machine ids referencing each.
		var stationToMachines = new Dictionary<string, List<string>>();
		foreach (var def in MachineRegistry.All)
		{
			var rt = def.RecipeType;
			if (rt is null) continue;   // non-recipe machine (transformer, drum, lamp, ...)
			if (!stationToMachines.TryGetValue(rt.RegistryName, out var list))
				stationToMachines[rt.RegistryName] = list = new List<string>();
			list.Add(def.Id);
		}

		int empty = 0;
		foreach (var (station, machines) in stationToMachines)
		{
			if (RecipeRegistry.ForStation(station).Count > 0) continue;
			// `dummy` is upstream's recipe-less placeholder (cleanroom etc.).
			if (station == "dummy") continue;
			empty++;
			mod.Logger.Warn(
				$"[recipe-coverage] station '{station}' has 0 recipes - machine(s) " +
				$"[{string.Join(", ", machines)}] will idle. Verify the GTRecipeType " +
				$"registry name matches upstream's recipe `type`.");
		}
		mod.Logger.Info(
			$"[recipe-coverage] {stationToMachines.Count} recipe stations checked, {empty} empty.");
	}
}
