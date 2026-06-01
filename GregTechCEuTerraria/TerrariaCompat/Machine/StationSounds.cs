#nullable enable
using System.Collections.Generic;
using Terraria.Audio;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Per-station loop sound (upstream GTRecipeTypes.setSound). Volume kept low so
// clustered machines don't pile up. Assets verbatim from upstream gtceu sounds.
public static class StationSounds
{
	// MaxInstances=3 caps voices per family; ReplaceOldest keeps near audible;
	// PauseWithGame yields FAudio voices on alt-tab.
	private static SoundStyle Loop(string assetName, float volume = 0.45f) =>
		new($"GregTechCEuTerraria/Content/Sounds/{assetName}")
		{
			Volume = volume,
			IsLooped = true,
			MaxInstances = 3,
			SoundLimitBehavior = SoundLimitBehavior.ReplaceOldest,
			PauseBehavior = PauseBehavior.PauseWithGame,
		};

	// One-shot finish ping.
	private static SoundStyle OneShot(string assetName, float volume = 0.6f) =>
		new($"GregTechCEuTerraria/Content/Sounds/{assetName}")
		{
			Volume = volume,
			IsLooped = false,
			MaxInstances = 3,
			SoundLimitBehavior = SoundLimitBehavior.ReplaceOldest,
		};

	// Verbatim from GTRecipeTypes.java setSound.
	public static readonly IReadOnlyDictionary<string, SoundStyle> LoopForStation = new Dictionary<string, SoundStyle>
	{
		// FURNACE family
		["alloy_smelter"]             = Loop("furnace"),
		["electric_furnace"]          = Loop("furnace"),
		["steam_boiler"]              = Loop("furnace"),
		["autoclave"]                 = Loop("furnace"),

		// MACERATOR
		["macerator"]                 = Loop("macerator"),

		// ASSEMBLER family
		["assembler"]                 = Loop("assembler"),
		["packer"]                    = Loop("assembler"),
		["circuit_assembler"]         = Loop("assembler"),

		// CHEMICAL family
		["brewery"]                   = Loop("chemical"),
		["chemical_reactor"]          = Loop("chemical"),
		["fermenter"]                 = Loop("chemical"),

		// BATH / wet
		["chemical_bath"]             = Loop("bath"),
		["canner"]                    = Loop("bath"),
		["mixer"]                     = Loop("mixer"),
		["ore_washer"]                = Loop("bath"),

		// Crushing / pressing
		["compressor"]                = Loop("compressor"),
		["extruder"]                  = Loop("compressor"),
		["forming_press"]             = Loop("compressor"),
		["forge_hammer"]              = Loop("forge_hammer"),
		// EXTRACTOR has no upstream sound; reuse compressor so it's not silent.
		["extractor"]                 = Loop("compressor"),

		// Cutting
		["cutter"]                    = Loop("cut"),
		["lathe"]                     = Loop("cut"),

		// Electrical
		["electrolyzer"]              = Loop("electrolyzer"),
		["arc_furnace"]               = Loop("arc"),
		["plasma_arc_furnace"]        = Loop("arc"),
		["polarizer"]                 = Loop("arc"),
		["electromagnetic_separator"] = Loop("arc"),
		["laser_engraver"]            = Loop("electrolyzer"),
		["scanner"]                   = Loop("electrolyzer"),

		// Motors / wires
		["motor"]                     = Loop("motor"),
		["bender"]                    = Loop("motor"),
		["wiremill"]                  = Loop("motor"),

		// Fluid / thermal
		["boiler"]                    = Loop("boiler"),
		["coal_boiler"]               = Loop("boiler"),
		["distillery"]                = Loop("boiler"),
		["fluid_heater"]              = Loop("boiler"),
		["fluid_solidifier"]          = Loop("cooling"),
		["gas_collector"]             = Loop("cooling"),
		["air_scrubber"]              = Loop("cooling"),
		["vacuum_freezer"]            = Loop("cooling"),

		// Centrifuges
		["centrifuge"]                = Loop("centrifuge"),
		["thermal_centrifuge"]        = Loop("centrifuge"),
		// Upstream SAND_PLACE is one-shot; centrifuge loop substitutes.
		["sifter"]                    = Loop("centrifuge"),

		["steam_turbine"]             = Loop("turbine"),
		["gas_turbine"]               = Loop("turbine"),
		["combustion_generator"]      = Loop("combustion"),

		// Misc
		["rock_breaker"]              = Loop("fire"),
		["research_station"]          = Loop("computation"),

		// Multi controllers.
		["electric_blast_furnace"]    = Loop("furnace"),
		// No upstream sound; reuse EBF family for consistency.
		["alloy_blast_smelter"]       = Loop("furnace"),
		["large_boiler"]              = Loop("furnace"),
		["coke_oven"]                 = Loop("fire"),
		["primitive_blast_furnace"]   = Loop("fire"),
		["pyrolyse_oven"]             = Loop("fire"),
		["cracker"]                   = Loop("fire"),
		["distillation_tower"]        = Loop("chemical"),
		["large_chemical_reactor"]    = Loop("chemical"),
		["assembly_line"]             = Loop("assembler"),
		// Upstream IMPLOSION uses GENERIC_EXPLODE one-shot; 20-tick duration
		// means a looped explosion ~ one bang per recipe. SoundID.Item14 (bomb)
		// matches; `with` adds loop / pause / cap defaults from Loop().
		["implosion_compressor"]      = SoundID.Item14 with
		{
			Volume             = 0.55f,
			IsLooped           = true,
			MaxInstances       = 3,
			SoundLimitBehavior = SoundLimitBehavior.ReplaceOldest,
			PauseBehavior      = PauseBehavior.PauseWithGame,
		},
	};

	public static readonly SoundStyle DefaultFinish = OneShot("furnace", 0.5f);

	public static SoundStyle? TryGetLoop(string stationId) =>
		LoopForStation.TryGetValue(stationId, out var s) ? s : null;
}
