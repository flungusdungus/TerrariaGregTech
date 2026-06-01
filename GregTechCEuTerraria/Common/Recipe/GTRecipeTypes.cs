#nullable enable
using GregTechCEuTerraria.Api.Recipe;

namespace GregTechCEuTerraria.Common.Recipe;

// LOCKED - port of com.gregtechceu.gtceu.api.recipe.GTRecipeTypes (the
// static-fields registry). One static GTRecipeType holder per known
// machine station id. Subclasses of WorkableTieredMachine override
// `GetRecipeType()` to return the corresponding constant rather than
// hardcoding a string - matches upstream's pattern verbatim and avoids
// the per-instance GTRecipeType cache.
//
// Upstream has ~80 entries (every recipe type the mod ships). We only
// list the ones our PMT subclasses currently bind to + the few non-PMT
// stations the recipe browser also surfaces (steam_boiler, steam_turbine).
// Add new entries when porting new machines.
public static class GTRecipeTypes
{
	public static readonly GTRecipeType MACERATOR                 = GTRecipeType.GetOrCreate("macerator");
	public static readonly GTRecipeType ALLOY_SMELTER             = GTRecipeType.GetOrCreate("alloy_smelter");
	public static readonly GTRecipeType ASSEMBLER                 = GTRecipeType.GetOrCreate("assembler");
	public static readonly GTRecipeType ASSEMBLY_LINE             = GTRecipeType.GetOrCreate("assembly_line");
	public static readonly GTRecipeType AUTOCLAVE                 = GTRecipeType.GetOrCreate("autoclave");
	public static readonly GTRecipeType BENDER                    = GTRecipeType.GetOrCreate("bender");
	public static readonly GTRecipeType BREWERY                   = GTRecipeType.GetOrCreate("brewery");
	public static readonly GTRecipeType CANNER                    = GTRecipeType.GetOrCreate("canner");
	public static readonly GTRecipeType CHEMICAL_BATH             = GTRecipeType.GetOrCreate("chemical_bath");
	public static readonly GTRecipeType CENTRIFUGE                = GTRecipeType.GetOrCreate("centrifuge");
	public static readonly GTRecipeType CHEMICAL_REACTOR          = GTRecipeType.GetOrCreate("chemical_reactor");
	public static readonly GTRecipeType CIRCUIT_ASSEMBLER         = GTRecipeType.GetOrCreate("circuit_assembler");
	public static readonly GTRecipeType COMPRESSOR                = GTRecipeType.GetOrCreate("compressor");
	public static readonly GTRecipeType DISTILLERY                = GTRecipeType.GetOrCreate("distillery");
	public static readonly GTRecipeType DISTILLATION_TOWER        = GTRecipeType.GetOrCreate("distillation_tower");
	public static readonly GTRecipeType ELECTROLYZER              = GTRecipeType.GetOrCreate("electrolyzer");
	public static readonly GTRecipeType ELECTROMAGNETIC_SEPARATOR = GTRecipeType.GetOrCreate("electromagnetic_separator");
	public static readonly GTRecipeType EXTRACTOR                 = GTRecipeType.GetOrCreate("extractor");
	public static readonly GTRecipeType FERMENTER                 = GTRecipeType.GetOrCreate("fermenter");
	public static readonly GTRecipeType FLUID_HEATER              = GTRecipeType.GetOrCreate("fluid_heater");
	public static readonly GTRecipeType FLUID_SOLIDIFIER          = GTRecipeType.GetOrCreate("fluid_solidifier");
	public static readonly GTRecipeType FORGE_HAMMER              = GTRecipeType.GetOrCreate("forge_hammer");
	public static readonly GTRecipeType FORMING_PRESS             = GTRecipeType.GetOrCreate("forming_press");
	public static readonly GTRecipeType LATHE                     = GTRecipeType.GetOrCreate("lathe");
	public static readonly GTRecipeType MIXER                     = GTRecipeType.GetOrCreate("mixer");
	public static readonly GTRecipeType ORE_WASHER                = GTRecipeType.GetOrCreate("ore_washer");
	public static readonly GTRecipeType PACKER                    = GTRecipeType.GetOrCreate("packer");
	public static readonly GTRecipeType POLARIZER                 = GTRecipeType.GetOrCreate("polarizer");
	public static readonly GTRecipeType WIREMILL                  = GTRecipeType.GetOrCreate("wiremill");

	// Additional single processing machines.
	public static readonly GTRecipeType ELECTRIC_FURNACE          = GTRecipeType.GetOrCreate("electric_furnace");
	public static readonly GTRecipeType ARC_FURNACE               = GTRecipeType.GetOrCreate("arc_furnace");
	public static readonly GTRecipeType CUTTER                    = GTRecipeType.GetOrCreate("cutter");
	public static readonly GTRecipeType EXTRUDER                  = GTRecipeType.GetOrCreate("extruder");
	public static readonly GTRecipeType SCANNER                   = GTRecipeType.GetOrCreate("scanner");
	public static readonly GTRecipeType LASER_ENGRAVER            = GTRecipeType.GetOrCreate("laser_engraver");
	public static readonly GTRecipeType SIFTER                    = GTRecipeType.GetOrCreate("sifter");
	public static readonly GTRecipeType THERMAL_CENTRIFUGE        = GTRecipeType.GetOrCreate("thermal_centrifuge");
	public static readonly GTRecipeType GAS_COLLECTOR             = GTRecipeType.GetOrCreate("gas_collector");
	public static readonly GTRecipeType AIR_SCRUBBER              = GTRecipeType.GetOrCreate("air_scrubber");
	// Rock Crusher's recipe type - upstream registry name is `rock_breaker`.
	public static readonly GTRecipeType ROCK_BREAKER             = GTRecipeType.GetOrCreate("rock_breaker");

	// Non-PMT stations referenced by the recipe browser / generator entities.
	public static readonly GTRecipeType STEAM_BOILER              = GTRecipeType.GetOrCreate("steam_boiler");
	public static readonly GTRecipeType COKE_OVEN                 = GTRecipeType.GetOrCreate("coke_oven");
	public static readonly GTRecipeType PRIMITIVE_BLAST_FURNACE   = GTRecipeType.GetOrCreate("primitive_blast_furnace");
	public static readonly GTRecipeType LARGE_BOILER              = GTRecipeType.GetOrCreate("large_boiler");
	public static readonly GTRecipeType STEAM_TURBINE             = GTRecipeType.GetOrCreate("steam_turbine");
	public static readonly GTRecipeType GAS_TURBINE               = GTRecipeType.GetOrCreate("gas_turbine");
	// Registry name MUST be the upstream recipe `type` (station id). Upstream's
	// combustion-generator recipes are typed `gtceu:combustion_generator` -
	// keying this "combustion" silently matched zero recipes.
	public static readonly GTRecipeType COMBUSTION                = GTRecipeType.GetOrCreate("combustion_generator");
	// Plasma Generator - `gtceu:plasma_generator` recipe station. Upstream's
	// PLASMA_GENERATOR_FUELS, consumed by the singleblock plasma_generator (not
	// ported as a singleblock - generator multis only) and by the
	// large_plasma_turbine multi.
	public static readonly GTRecipeType PLASMA_GENERATOR          = GTRecipeType.GetOrCreate("plasma_generator");

	// Stand-in recipe type for multiblocks that run custom recipe-less logic
	// (cleanroom - uses CleanroomLogic's cleanliness ratchet instead of
	// actual GTRecipes). Matches upstream `DUMMY_RECIPES`. Carries no
	// registered recipes; `RecipeRegistry.Get(DUMMY)` returns an empty list.
	public static readonly GTRecipeType DUMMY                     = GTRecipeType.GetOrCreate("dummy");

	// Browser-display stations for the world-I/O multis. NOT scanned by
	// RecipeLogic (`LargeMinerMachine` / `FluidDrillingRigMachine` override
	// `IsRecipeLogicAvailable()` to false and drive themselves off OnTick).
	// Populated at `Mod.Load` by `BiomeWorldIORecipeSynth` - one synthetic
	// recipe per biome per station, so the recipe browser surfaces
	// "Forest -> copper / tin / iron / gold / apatite" etc. Adapted (no upstream
	// equivalent - upstream's miner walks real ore blocks; rig uses bedrock
	// fluid veins, neither registers GTRecipes).
	public static readonly GTRecipeType LARGE_MINER               = GTRecipeType.GetOrCreate("large_miner");
	public static readonly GTRecipeType FLUID_DRILLING_RIG        = GTRecipeType.GetOrCreate("fluid_drilling_rig");

	// Fusion reactor recipes - consumed by the LuV/ZPM/UV fusion_reactor multi.
	// Recipes carry `eu_to_start` in their data tag; the FUSION_OC modifier
	// (GTRecipeModifiers) pays that cost from the controller's capacitor.
	public static readonly GTRecipeType FUSION_REACTOR            = GTRecipeType.GetOrCreate("fusion_reactor");

	// Research Station recipes - upstream `RESEARCH_STATION_RECIPES`. Consume
	// CWU/t (the `cwu` tick-input) + EU + a data orb/module + the item under
	// research, producing the researched data item (NBT-stamped). Driven by
	// ResearchStationMachine's custom recipe logic (duration_is_total_cwu).
	public static readonly GTRecipeType RESEARCH_STATION          = GTRecipeType.GetOrCreate("research_station");

	// Electric Blast Furnace recipes - gated on `ebf_temp` data field via
	// `EBF_OVERCLOCK`. Recipe station id matches upstream `BLAST_RECIPES`.
	public static readonly GTRecipeType BLAST_RECIPES             = GTRecipeType.GetOrCreate("electric_blast_furnace");

	// Large Chemical Reactor - `LARGE_CHEMICAL_RECIPES` upstream. Perfect
	// subtick overclock applied (no custom modifier).
	public static readonly GTRecipeType LARGE_CHEMICAL_RECIPES    = GTRecipeType.GetOrCreate("large_chemical_reactor");

	// === Standard processing multis (no custom modifier) ====================
	// Each station id matches upstream's recipe `type` field; recipes pool
	// with the singleblock version sharing the same id (e.g. large_centrifuge
	// runs CENTRIFUGE recipes - these multis don't have their OWN id; they
	// reuse the singleblock id). The few that DO have their own station -
	// vacuum_freezer, implosion_compressor, alloy_blast_smelter - are listed
	// here. Same NON_PERFECT_SUBTICK overclock applies to all of them.
	public static readonly GTRecipeType VACUUM_RECIPES            = GTRecipeType.GetOrCreate("vacuum_freezer");
	public static readonly GTRecipeType IMPLOSION_RECIPES         = GTRecipeType.GetOrCreate("implosion_compressor");
	public static readonly GTRecipeType ALLOY_BLAST_RECIPES       = GTRecipeType.GetOrCreate("alloy_blast_smelter");

	// === Coil-based multis (CoilWorkableElectricMultiblockMachine) ===========
	// Each station is upstream's own recipe-type id (NOT the multi's id where
	// they differ - pyrolyse_oven shares its station with itself, cracker
	// similarly). Recipe modifier supplied per-MachineDefinition row.
	public static readonly GTRecipeType PYROLYSE_RECIPES          = GTRecipeType.GetOrCreate("pyrolyse_oven");
	public static readonly GTRecipeType CRACKING_RECIPES          = GTRecipeType.GetOrCreate("cracker");
}
