#nullable enable
using Terraria.Localization;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

// Locale-fallback installer for the multiblock display-text keys consulted by
// MultiblockDisplayText. Keys live in raw gtceu.* (matching upstream
// en_us.json) so the builder's GetTextValue calls grep 1:1 against upstream
// Java. port-locale.py mirrors under Mods.GregTechCEuTerraria.* and doesn't
// own this namespace, so we install English fallbacks via GetOrRegister.
//
// Format adaptations: %s -> {0}/{1}/...; MC section-color -> Terraria [c/RRGGBB:...]
// (sectiona->55FF55, sectionc->FF5555, section6->FFAA00, sectione->FFFF55, sectionb->55FFFF, section7->AAAAAA).
public static class MultiblockLocale
{
	public static void RegisterAll()
	{
		// Plug TerrariaCompat resolver into the Api builder so AddRecipeFailReasonLine
		// maps raw gtceu.recipe_logic.* ids to the same RecipeStatus strings the
		// world-hover tooltip uses (single source of truth).
		Api.Machine.Multiblock.MultiblockDisplayText.FailReasonResolver =
			RecipeStatusText.Resolve;

		Register("gtceu.multiblock.invalid_structure",         "[c/FF5555:Invalid Structure!]");
		Register("gtceu.multiblock.invalid_structure.tooltip", "Make sure the structure is built correctly.");

		Register("gtceu.multiblock.max_energy_per_tick",       "Max Energy: [c/55FF55:{0}] EU/t [c/55FFFF:({1})]");
		Register("gtceu.multiblock.max_recipe_tier",           "Recipe Tier: [c/55FF55:{0}]");
		Register("gtceu.multiblock.energy_consumption",        "Energy: [c/55FF55:{0}] EU/t [c/55FFFF:({1})]");
		Register("gtceu.multiblock.max_energy_per_tick_amps",  "Max Energy: [c/55FF55:{0}] EU/t [c/55FF55:{1}] A [c/55FFFF:({2})]");

		Register("gtceu.multiblock.computation.max",           "Max Computation: [c/55FF55:{0}] CWU/t");
		Register("gtceu.multiblock.computation.usage",         "Computation: [c/55FF55:{0}]");
		Register("gtceu.multiblock.computation.not_enough_computation", "[c/FF5555:Not Enough Computation!]");

		Register("gtceu.multiblock.idling",                    "[c/FFFF55:Idling]");
		Register("gtceu.multiblock.work_paused",               "[c/FFFF55:Work Paused]");
		Register("gtceu.multiblock.running",                   "[c/55FF55:Running Perfectly]");
		// Passive-provider "working" key (HPCA / Data Bank / Network Switch).
		Register("gtceu.multiblock.data_bank.providing",       "[c/55FF55:Providing]");
		// Research Station "working" key (verbatim upstream addDisplayText key).
		Register("gtceu.multiblock.research_station.researching", "[c/55FF55:Researching]");

		Register("gtceu.multiblock.progress",                  "Progress: [c/FFAA00:{0} s] / [c/FFAA00:{1} s] [c/55FF55:({2}%)]");
		Register("gtceu.multiblock.progress_percent",          "Progress: [c/FFAA00:{0}%]");

		Register("gtceu.recipe_logic.setup_fail",              "[c/FF5555:Invalid Recipe!]");

		Register("gtceu.multiblock.batch_enabled",             "Batch Size: [c/55FF55:{0}]");
		Register("gtceu.multiblock.subtick_parallels",         "Subtick Parallels: [c/55FF55:{0}]");
		Register("gtceu.multiblock.total_runs",                "Recipe Output Multiplier: [c/55FF55:{0}]x");

		Register("gtceu.multiblock.parallel",                  "Parallel: [c/FFAA00:{0}]");
		Register("gtceu.multiblock.parallel.exact",            "Parallel: [c/FFAA00:{0}x]");

		Register("gtceu.gui.machinemode",                      "Mode: [c/55FF55:{0}]");

		Register("gtceu.multiblock.not_enough_energy",         "[c/FF5555:Not Enough Energy!]");
		Register("gtceu.multiblock.not_enough_energy_output",  "[c/FF5555:Dynamo Tier Too Low!]");

		Register("gtceu.multiblock.universal.has_problems",           "[c/FF5555:Machine has problems!]");
		Register("gtceu.multiblock.universal.problem.wrench",         "  [c/FFAA00:Loose pipes and screws.]");
		Register("gtceu.multiblock.universal.problem.screwdriver",    "  [c/FFAA00:Things are not in place.]");
		Register("gtceu.multiblock.universal.problem.soft_mallet",    "  [c/FFAA00:Something is stuck.]");
		Register("gtceu.multiblock.universal.problem.hard_hammer",    "  [c/FFAA00:Mechanical parts need adjustment.]");
		Register("gtceu.multiblock.universal.problem.wire_cutter",    "  [c/FFAA00:Wires need to be reconnected.]");
		Register("gtceu.multiblock.universal.problem.crowbar",        "  [c/FFAA00:Rotors need maintenance.]");

		Register("gtceu.multiblock.universal.muffler_obstructed",         "[c/FF5555:Muffler Hatch is obstructed!]");
		Register("gtceu.multiblock.universal.muffler_obstructed.tooltip", "Ensure the muffler has air space above.");

		Register("gtceu.multiblock.turbine.fuel_needed",          "Needs [c/55FF55:{0}] every [c/55FF55:{1}] ticks");
		Register("gtceu.multiblock.turbine.energy_per_tick_maxed","Producing [c/55FF55:{0}] EU/t");

		Register("gtceu.multiblock.output_line.0",                "Outputs [c/55FF55:{0}] [c/55FFFF:{1}] every [c/FFAA00:{2}] sec");
		Register("gtceu.multiblock.output_line.1",                "Outputs [c/55FF55:{0}] [c/55FFFF:{1}] [c/FFAA00:{2}]x per sec");
		Register("gtceu.multiblock.output_line.2",                "Outputs ~[c/55FF55:{0}] [c/55FFFF:{1}] every [c/FFAA00:{2}] sec");
		Register("gtceu.multiblock.output_line.3",                "Outputs ~[c/55FF55:{0}] [c/55FFFF:{1}] [c/FFAA00:{2}]x per sec");

		Register("gtceu.gui.content.range",                       "{0}-{1}");

		// Coil-multi additionalDisplay keys (verbatim upstream en_us.json).
		// blast_furnace.max_temperature has no color - upstream colors at call site.
		Register("gtceu.multiblock.blast_furnace.max_temperature",      "Heat Capacity: {0}");
		Register("gtceu.multiblock.pyrolyse_oven.speed",                "Speed: [c/55FF55:{0}]%");
		Register("gtceu.multiblock.multi_furnace.heating_coil_level",   "Heating Coil Level: [c/55FF55:{0}]");
		Register("gtceu.multiblock.multi_furnace.heating_coil_discount","Energy Discount: [c/55FF55:{0}]x");
		Register("gtceu.multiblock.cracking_unit.energy",               "Energy: [c/55FF55:{0}]%");

		// SteamParallel (SteamParallelMultiblockMachine.java:132-162).
		Register("gtceu.multiblock.steam.steam_stored",                 "Stored Steam: [c/55FF55:{0}] / [c/55FF55:{1}] L");
		Register("gtceu.multiblock.steam.low_steam",                    "[c/FF5555:Not Enough Steam!]");

		// LargeBoiler (LargeBoilerMachine.java:199-220). Temperature args are
		// currentTemperature + 274 and maxTemperature + 274 (degC delta -> K).
		Register("gtceu.multiblock.large_boiler.temperature",           "Temperature: [c/55FF55:{0}] K / [c/55FF55:{1}] K");
		Register("gtceu.multiblock.large_boiler.steam_output",          "Steam Output: [c/55FF55:{0}] L/t");
		// Throttle arg arrives pre-wrapped in a color tag - nesting would render literal.
		Register("gtceu.multiblock.large_boiler.throttle",              "Throttle: {0}");
		Register("gtceu.multiblock.large_boiler.throttle_modify",       "Adjust Throttle:");

		// Cleanroom (CleanroomMachine.java:451-500).
		Register("gtceu.recipe.cleanroom.display_name",                 "Cleanroom");
		Register("gtceu.recipe.cleanroom_sterile.display_name",         "Sterile Cleanroom");
		Register("gtceu.multiblock.waiting",                            "[c/FF5555:Waiting]");
		Register("gtceu.multiblock.cleanroom.clean_state",              "[c/55FF55:Clean]");
		Register("gtceu.multiblock.cleanroom.dirty_state",              "[c/FF5555:Dirty]");
		Register("gtceu.multiblock.cleanroom.clean_amount",             "Cleanliness: [c/55FF55:{0}]%");
		// DEVIATION: upstream's dimensions.1 is
		// LxHxF (3D); our .2d variant carries LxH only.
		Register("gtceu.multiblock.dimensions.0",                       "Dimensions:");
		Register("gtceu.multiblock.dimensions.1.2d",                    "[c/55FF55:{0}]x[c/55FF55:{1}]");
	}

	private static void Register(string key, string english) =>
		Language.GetOrRegister(key, () => english);
}
