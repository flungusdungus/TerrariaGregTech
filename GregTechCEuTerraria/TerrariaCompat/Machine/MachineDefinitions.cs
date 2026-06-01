#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Pattern;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.Common.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// The machine-definition table - one row per machine kind. This is the data
// that used to be scattered across ~36 thin *TileEntity.cs subclasses (recipe
// type, slot/tank counts, steam/battery params). Mirrors upstream GregTech's
// registerSimpleMachines data rows.
//
// RegisterAll() runs once at Mod.Load, BEFORE any machine tile/item registers,
// so MachineRegistry is fully populated when TieredMachineFactory enumerates it.
public static class MachineDefinitions
{
	// Every voltage tier - matches TieredMachineFactory.AllTiers. Computed
	// locally so this table stays free of any tML dependency.
	private static readonly VoltageTier[] AllTiers =
		(VoltageTier[])Enum.GetValues(typeof(VoltageTier));

	// Steam boilers aren't voltage-tiered - they register a single variant.
	// A 1-element list keeps them in the uniform "one tile+item per tier" loop.
	private static readonly VoltageTier[] OneTier = { VoltageTier.LV };
	// MAX tier "pin" for creative debug machines. Renders with the purple MAX
	// voltage casing (block/casings/voltage/max/side) - upstream's creative_chest
	// / creative_tank / creative_energy all parent off the MAX casing.
	private static readonly VoltageTier[] MaxTier = { VoltageTier.MAX };

	// Transformer registration range: ULV..OpV. A transformer steps
	// tier<->tier+1, so the top tier (MAX) has nothing to step to. Mirrors
	// TransformerMachine.Tiers (inlined to keep this table tML-free).
	private static readonly VoltageTier[] TransformerTiers =
	{
		VoltageTier.ULV, VoltageTier.LV,  VoltageTier.MV,  VoltageTier.HV,
		VoltageTier.EV,  VoltageTier.IV,  VoltageTier.LuV, VoltageTier.ZPM,
		VoltageTier.UV,  VoltageTier.UHV, VoltageTier.UEV, VoltageTier.UIV,
		VoltageTier.UXV, VoltageTier.OpV,
	};

	// The Quantum Tank / Quantum Chest family splits its voltage range across
	// two upstream ids: LV..EV register as `super_tank` / `super_chest`,
	// IV..OpV as `quantum_tank` / `quantum_chest` - same machine family, two
	// ids. (Matches the exact upstream-registered set; no ULV / MAX variant.)
	private static readonly VoltageTier[] SuperTiers =
		{ VoltageTier.LV, VoltageTier.MV, VoltageTier.HV, VoltageTier.EV };

	private static readonly VoltageTier[] QuantumTiers =
	{
		VoltageTier.IV,  VoltageTier.LuV, VoltageTier.ZPM, VoltageTier.UV,
		VoltageTier.UHV, VoltageTier.UEV, VoltageTier.UIV, VoltageTier.UXV,
		VoltageTier.OpV,
	};

	// Combustion / Gas Turbine generators are LV..HV only (upstream).
	private static readonly VoltageTier[] GeneratorTiers =
		{ VoltageTier.LV, VoltageTier.MV, VoltageTier.HV };

	// Fisher tiers - upstream GTMachines.FISHER registers LV..LuV (6 tiers).
	private static readonly VoltageTier[] FisherTiers =
	{
		VoltageTier.LV, VoltageTier.MV, VoltageTier.HV,
		VoltageTier.EV, VoltageTier.IV, VoltageTier.LuV,
	};

	// World Accelerator tiers - upstream GTMachines.WORLD_ACCELERATOR (LV..UV).
	private static readonly VoltageTier[] WorldAcceleratorTiers =
	{
		VoltageTier.LV,  VoltageTier.MV,  VoltageTier.HV, VoltageTier.EV,
		VoltageTier.IV,  VoltageTier.LuV, VoltageTier.ZPM, VoltageTier.UV,
	};

	// Block Breaker tiers - LV..ZPM (7 tiers). Range doubles per tier
	// (16 << tier), so ZPM = 2048 tiles, enough to drill any world size
	// surface-to-Hell in one placement. Past ZPM range exceeds world bounds.
	private static readonly VoltageTier[] BlockBreakerTiers =
	{
		VoltageTier.LV,  VoltageTier.MV, VoltageTier.HV,
		VoltageTier.EV,  VoltageTier.IV, VoltageTier.LuV,
		VoltageTier.ZPM,
	};

	// Multi-hatch / hi-amp tiers: upstream `MULTI_HATCH_TIERS =
	// GTValues.tiersBetween(EV, isHighTier ? MAX : UHV)` (GTMachineUtils.java:102).
	// Upstream's `runData` always runs with isHighTier=true (the recipes we
	// ship were generated under that flag), so we mirror the high-tier range:
	// EV..MAX (10 tiers). Shared by 4x/9x fluid hatches and the 4A / 16A / 64A
	// energy hatch variants.
	private static readonly VoltageTier[] MultiHatchTiers =
	{
		VoltageTier.EV,  VoltageTier.IV,  VoltageTier.LuV, VoltageTier.ZPM,
		VoltageTier.UV,  VoltageTier.UHV, VoltageTier.UEV, VoltageTier.UIV,
		VoltageTier.UXV, VoltageTier.MAX,
	};

	// Dual-hatch tiers: upstream `DUAL_HATCH_TIERS = GTValues.tiersBetween(LuV,
	// isHighTier ? MAX : UHV)` (GTMachineUtils.java:103). High-tier range:
	// LuV..MAX (9 tiers, including OpV - confirmed against the registry dump's
	// luv..max dual hatch ids). Used by dual_input_hatch / dual_output_hatch.
	private static readonly VoltageTier[] DualHatchTiers =
	{
		VoltageTier.LuV, VoltageTier.ZPM, VoltageTier.UV,  VoltageTier.UHV,
		VoltageTier.UEV, VoltageTier.UIV, VoltageTier.UXV, VoltageTier.OpV,
		VoltageTier.MAX,
	};

	// Laser-hatch tiers: upstream `HIGH_TIERS = GTValues.tiersBetween(IV,
	// isHighTier ? OpV : UHV)` (GTMachineUtils.java:101). High-tier range:
	// IV..OpV (9 tiers). We extend to MAX for symmetry with MultiHatchTiers.
	// Used by all 6 laser hatch SKU families (3 amps x 2 directions).
	private static readonly VoltageTier[] LaserHatchTiers =
	{
		VoltageTier.IV,  VoltageTier.LuV, VoltageTier.ZPM, VoltageTier.UV,
		VoltageTier.UHV, VoltageTier.UEV, VoltageTier.UIV, VoltageTier.UXV,
		VoltageTier.OpV, VoltageTier.MAX,
	};

	// Electric tiers: upstream `ELECTRIC_TIERS = GTValues.tiersBetween(LV,
	// isHighTier ? OpV : UV)` (GTMachineUtils.java:99). High-tier range:
	// LV..OpV (13 tiers, no ULV, no MAX). Used by the muffler hatch and
	// diodes (GTMachines.java:817).
	private static readonly VoltageTier[] ElectricTiers =
	{
		VoltageTier.LV,  VoltageTier.MV,  VoltageTier.HV,  VoltageTier.EV,
		VoltageTier.IV,  VoltageTier.LuV, VoltageTier.ZPM, VoltageTier.UV,
		VoltageTier.UHV, VoltageTier.UEV, VoltageTier.UIV, VoltageTier.UXV,
		VoltageTier.OpV,
	};

	public static void RegisterAll()
	{
		MachineRegistry.Clear();

		// === Hull - tiered passthrough (crafting intermediate) ==============
		// Upstream GTMachines.HULL - every real machine is assembled from a
		// machine_hull of its tier. Energy passthrough (V[tier]*16 buffer,
		// 1A in/out), no GUI. Upstream's `.overlayTieredHullModel("hull")`
		// resolves to `block/machine/part/hull.json`, parent
		// `block/overlay/2_layer/tinted/front` -> tier casing + tinted overlay
		// (tintindex 2, tier color) + directional overlay. No emissive layer.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "machine_hull", Label = "Machine Hull",
			Family = MachineFamily.Hull,
			Tiers = AllTiers,
			Casing = MachineCasing.Voltage,
			OverlayDir = PartOverlayDir,
			TintedOverlayBasename = "overlay_energy_1a_tinted",
			OverlayBasename       = "overlay_energy_1a_out",
			LayoutKey = "none",
		});

		// === WorkableTiered - recipe-driven processing machines =============
		// Slot/tank counts audited against upstream setMaxIOSize. LayoutKey is
		// "generic" except where a bespoke layout exists.
		Wtm("macerator",   "Macerator",   GTRecipeTypes.MACERATOR, 1, 4);
		Wtm("alloy_smelter", "Alloy Smelter", GTRecipeTypes.ALLOY_SMELTER, 2, 1, circuit: true);
		Wtm("brewery",     "Brewery",     GTRecipeTypes.BREWERY,   1, 0, inTanks: 1, outTanks: 1);
		Wtm("compressor",  "Compressor",  GTRecipeTypes.COMPRESSOR, 1, 1);
		Wtm("extractor",   "Extractor",   GTRecipeTypes.EXTRACTOR,  1, 1, outTanks: 1);
		Wtm("forge_hammer", "Forge Hammer", GTRecipeTypes.FORGE_HAMMER, 1, 1);
		Wtm("bender",      "Bender",      GTRecipeTypes.BENDER,     2, 1, circuit: true);
		Wtm("lathe",       "Lathe",       GTRecipeTypes.LATHE,      1, 2);
		Wtm("polarizer",   "Polarizer",   GTRecipeTypes.POLARIZER,  1, 1);
		Wtm("wiremill",    "Wiremill",    GTRecipeTypes.WIREMILL,   2, 1, circuit: true);
		Wtm("electromagnetic_separator", "Electromagnetic Separator",
			GTRecipeTypes.ELECTROMAGNETIC_SEPARATOR, 1, 3);
		Wtm("packer",      "Packer",      GTRecipeTypes.PACKER,     2, 2, circuit: true);
		Wtm("fluid_solidifier", "Fluid Solidifier", GTRecipeTypes.FLUID_SOLIDIFIER, 1, 1, inTanks: 1);
		Wtm("chemical_reactor", "Chemical Reactor", GTRecipeTypes.CHEMICAL_REACTOR,
			2, 2, inTanks: 3, outTanks: 2, circuit: true);
		Wtm("electrolyzer", "Electrolyzer", GTRecipeTypes.ELECTROLYZER,
			2, 6, inTanks: 1, outTanks: 6, circuit: true);
		// Upstream CENTRIFUGE_RECIPES.setMaxIOSize(2, 6, 1, 6).
		Wtm("centrifuge",  "Centrifuge",  GTRecipeTypes.CENTRIFUGE,
			2, 6, inTanks: 1, outTanks: 6, circuit: true);
		// More single processing machines - IO straight from upstream
		// GTRecipeTypes.*.setMaxIOSize(itemIn, itemOut, fluidIn, fluidOut).
		Wtm("arc_furnace",        "Arc Furnace",        GTRecipeTypes.ARC_FURNACE,        1, 4, inTanks: 1, outTanks: 1);
		Wtm("cutter",             "Cutter",             GTRecipeTypes.CUTTER,             1, 2, inTanks: 1);
		Wtm("extruder",           "Extruder",           GTRecipeTypes.EXTRUDER,           2, 1);
		Wtm("scanner",            "Scanner",            GTRecipeTypes.SCANNER,            2, 1, inTanks: 1);
		Wtm("laser_engraver",     "Laser Engraver",     GTRecipeTypes.LASER_ENGRAVER,     2, 1);
		Wtm("sifter",             "Sifter",             GTRecipeTypes.SIFTER,             1, 6);
		Wtm("thermal_centrifuge", "Thermal Centrifuge", GTRecipeTypes.THERMAL_CENTRIFUGE, 1, 3);
		Wtm("gas_collector",      "Gas Collector",      GTRecipeTypes.GAS_COLLECTOR,      1, 0, outTanks: 1, circuit: true);
		Wtm("air_scrubber",       "Air Scrubber",       GTRecipeTypes.AIR_SCRUBBER,       1, 3, inTanks: 1, outTanks: 3);
		// Electric Furnace - IO 1/1; runs vanilla smelting upstream, so its own
		// recipe station carries no entries yet (smelting integration pending).
		Wtm("electric_furnace",   "Electric Furnace",   GTRecipeTypes.ELECTRIC_FURNACE,   1, 1);
		// Rock Crusher - recipe type `rock_breaker`, IO 1/4. Upstream gates each
		// recipe on an `adjacent_fluid` condition (a lava + water sandwich next
		// to the machine, like a cobble generator). That check is DELIBERATELY
		// NOT ported - placing lava AND water against a 2x2 machine is
		// impractical in Terraria's 2D world - so snapshot-recipes.py strips the
		// condition and the Rock Crusher runs its recipes unconditionally.
		Wtm("rock_crusher",       "Rock Crusher",       GTRecipeTypes.ROCK_BREAKER,       1, 4);
		Wtm("distillery",  "Distillery",  GTRecipeTypes.DISTILLERY, 1, 1, inTanks: 1, outTanks: 1, circuit: true);
		Wtm("mixer",       "Mixer",       GTRecipeTypes.MIXER,      6, 1, inTanks: 2, outTanks: 1, circuit: true);
		Wtm("autoclave",   "Autoclave",   GTRecipeTypes.AUTOCLAVE,  2, 2, inTanks: 1, outTanks: 1);
		Wtm("ore_washer",  "Ore Washer",  GTRecipeTypes.ORE_WASHER, 2, 3, inTanks: 1, circuit: true);
		Wtm("chemical_bath", "Chemical Bath", GTRecipeTypes.CHEMICAL_BATH, 1, 6, inTanks: 1, outTanks: 1);
		Wtm("fluid_heater", "Fluid Heater", GTRecipeTypes.FLUID_HEATER, 1, 0, inTanks: 1, outTanks: 1, circuit: true);
		Wtm("fermenter",   "Fermenter",   GTRecipeTypes.FERMENTER,  1, 1, inTanks: 1, outTanks: 1);
		Wtm("canner",      "Canner",      GTRecipeTypes.CANNER,     2, 2, inTanks: 1, outTanks: 1, circuit: true);
		Wtm("assembler",   "Assembler",   GTRecipeTypes.ASSEMBLER,  9, 1, inTanks: 1, circuit: true);
		Wtm("forming_press", "Forming Press", GTRecipeTypes.FORMING_PRESS, 6, 1, circuit: true);
		Wtm("circuit_assembler", "Circuit Assembler", GTRecipeTypes.CIRCUIT_ASSEMBLER, 6, 1, inTanks: 1, circuit: true);

		// === SimpleGenerator - recipe-driven generators =====================
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "steam_turbine", Label = "Steam Turbine",
			Family = MachineFamily.SimpleGenerator,
			Tiers = AllTiers,
			RecipeType = GTRecipeTypes.STEAM_TURBINE,
			InputSlotCount = 0, OutputSlotCount = 0,
			InputFluidTankCount = 1, OutputFluidTankCount = 1,
			OverlayDir = "block/generators/steam_turbine", OverlayBasename = "overlay_side",
			LayoutKey = "steam_turbine",
		});
		// Gas Turbine - GAS_TURBINE_FUELS.setMaxIOSize(0, 0, 1, 0): one fuel-gas
		// input tank, no output tank.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "gas_turbine", Label = "Gas Turbine",
			Family = MachineFamily.SimpleGenerator,
			Tiers = GeneratorTiers,
			RecipeType = GTRecipeTypes.GAS_TURBINE,
			InputSlotCount = 0, OutputSlotCount = 0,
			InputFluidTankCount = 1, OutputFluidTankCount = 0,
			OverlayDir = "block/generators/gas_turbine", OverlayBasename = "overlay_side",
			LayoutKey = "steam_turbine",
		});
		// Combustion Generator - burns a liquid fuel (one input tank). Runs the
		// upstream combustion_generator fuel recipes (diesel, ethanol, gasoline,
		// biodiesel, ...) - see GTRecipeTypes.COMBUSTION.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "combustion", Label = "Combustion Generator",
			Family = MachineFamily.SimpleGenerator,
			Tiers = GeneratorTiers,
			RecipeType = GTRecipeTypes.COMBUSTION,
			InputSlotCount = 0, OutputSlotCount = 0,
			InputFluidTankCount = 1, OutputFluidTankCount = 0,
			// Combustion ships only `overlay_top` (no side overlay), unlike the
			// steam/gas turbines - its working face is the top.
			OverlayDir = "block/generators/combustion", OverlayBasename = "overlay_top",
			LayoutKey = "steam_turbine",
		});

		// === SteamSolidBoiler - not voltage-tiered (single variant each) ====
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "lp_steam_solid_boiler", Label = "Coal Boiler",
			Family = MachineFamily.SteamSolidBoiler,
			Tiered = false, Tiers = OneTier,
			RecipeType = GTRecipeTypes.STEAM_BOILER,
			IsHighPressure = false,
			Casing = MachineCasing.BrickedBronze,
			OverlayDir = "block/generators/boiler/coal",
			LayoutKey = "coal_boiler",
		});
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "hp_steam_solid_boiler", Label = "HP Coal Boiler",
			Family = MachineFamily.SteamSolidBoiler,
			Tiered = false, Tiers = OneTier,
			RecipeType = GTRecipeTypes.STEAM_BOILER,
			IsHighPressure = true,
			Casing = MachineCasing.BrickedSteel,
			OverlayDir = "block/generators/boiler/coal",
			LayoutKey = "coal_boiler",
		});

		// === SteamSolarBoiler - sunlight-powered boilers =====================
		SolarBoiler("lp_steam_solar_boiler", "Solar Boiler",    highPressure: false);
		SolarBoiler("hp_steam_solar_boiler", "HP Solar Boiler", highPressure: true);

		// === SteamLiquidBoiler - liquid-fuel boilers (burn creosote / lava) ==
		LiquidBoiler("lp_steam_liquid_boiler", "Liquid Boiler",    highPressure: false);
		LiquidBoiler("hp_steam_liquid_boiler", "HP Liquid Boiler", highPressure: true);

		// === SimpleSteam - steam-powered processing machines ================
		// Each registers an LP + HP variant. They run the electric recipe
		// types (capped at LV, 2x duration on low-pressure) and pay the recipe
		// EU cost in steam - see SimpleSteamMachine. Item-only: no recipe fluid
		// handlers (upstream parity); the steam tank is the EU substitute.
		Steam("macerator",     "Macerator",     GTRecipeTypes.MACERATOR,        1, 4);
		Steam("compressor",    "Compressor",    GTRecipeTypes.COMPRESSOR,       1, 1);
		Steam("extractor",     "Extractor",     GTRecipeTypes.EXTRACTOR,        1, 1);
		Steam("alloy_smelter", "Alloy Smelter", GTRecipeTypes.ALLOY_SMELTER,    2, 1);
		Steam("forge_hammer",  "Forge Hammer",  GTRecipeTypes.FORGE_HAMMER,     1, 1);
		Steam("furnace",       "Furnace",       GTRecipeTypes.ELECTRIC_FURNACE, 1, 1);
		Steam("rock_crusher",  "Rock Crusher",  GTRecipeTypes.ROCK_BREAKER,     1, 4);

		// === SteamMiner - steam-powered auto-mining drill ===================
		// Two variants (lp/hp). Bespoke band-scan loop; no recipe type
		// (mining is world-driven, not recipe-driven - same shape as our
		// electric MinerMachine). The steam miner is a Terraria-compat
		// machine (no upstream singleblock equivalent - upstream's steam_miner
		// is a different shape), so we deliberately reuse the LV electric
		// miner's overlay art (`block/machines/miner/overlay_front` - the
		// proper big miner face plate) instead of upstream's `steam_miner`
		// PNG (a tiny dial / recipe-display thumbnail that reads as noise on
		// the brick casing).
		SteamMiner("lp_steam_miner", "Steam Miner",    overlayDir: "block/machines/miner", highPressure: false);
		SteamMiner("hp_steam_miner", "HP Steam Miner", overlayDir: "block/machines/miner", highPressure: true);

		// === BatteryBuffer - buffers + charger ==============================
		// Charger is just a buffer with OutputAmps == 0 (receive-only).
		Battery("battery_buffer_4x",  "4x Battery Buffer",  4,  BatteryBufferAmps.Normal,  outputAmps: 4);
		Battery("battery_buffer_8x",  "8x Battery Buffer",  8,  BatteryBufferAmps.Normal,  outputAmps: 8);
		Battery("battery_buffer_16x", "16x Battery Buffer", 16, BatteryBufferAmps.Normal,  outputAmps: 16);
		Battery("charger_4x",         "Turbo Charger",      4,  BatteryBufferAmps.Charger, outputAmps: 0);

		// === Transformer - 4 baseAmp variants, ULV..OpV =====================
		// No GUI (LayoutKey "none"); custom 2-face rendering lives on the
		// surviving TransformerTile/TransformerItem subclasses.
		Transformer("transformer_1a",  "Transformer",              1);
		Transformer("transformer_2a",  "Hi-Amp (2x) Transformer",  2);
		Transformer("transformer_4a",  "Hi-Amp (4x) Transformer",  4);
		Transformer("transformer_16a", "Power Transformer",        16);

		// === SolarPanel / Lamp / SuperTank - single-class families ==========
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "solar_panel_machine", Label = "Solar Panel",
			Family = MachineFamily.SolarPanel,
			Tiers = AllTiers,
			Casing = MachineCasing.None,
			OverlayDir = "block/generators/solar",
			LayoutKey = "none",
		});
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "lamp", Label = "Lamp",
			Family = MachineFamily.Lamp,
			Tiers = AllTiers,
			OverlayDir = "block", OverlayBasename = "yellow_lamp",
			LayoutKey = "none",
		});

		// === Long-distance pipeline endpoints (GUI-less, LV, screwdriver IO) ===
		// Single-tier LV blocks that cap an LD pipe run. No GUI; the player flips
		// IN/OUT with a screwdriver. Front overlay = the item/fluid hatch arrow.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "long_distance_item_pipeline_endpoint", Label = "Long Distance Item Pipeline Endpoint",
			Family = MachineFamily.LongDistanceItemEndpoint,
			Tiered = false, Tiers = OneTier,
			OverlayDir = "block/overlay/machine", OverlayBasename = "overlay_item_hatch_input",
			LayoutKey = "none",
		});
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "long_distance_fluid_pipeline_endpoint", Label = "Long Distance Fluid Pipeline Endpoint",
			Family = MachineFamily.LongDistanceFluidEndpoint,
			Tiered = false, Tiers = OneTier,
			OverlayDir = "block/overlay/machine", OverlayBasename = "overlay_fluid_hatch_input",
			LayoutKey = "none",
		});

		// === Fisher - upstream GTMachines.FISHER (LV..LuV) ===================
		// One MachineDefinition row spanning all six tiers - slot count, energy
		// draw, progress curve, water-check size all derive per-tier inside
		// FisherMachine. Texture set: upstream `createFisherModel` composites
		// `overlay/machine/overlay_screen` + `overlay/machine/overlay_qtank_emissive`
		// over the tiered hull (NO per-machine directory).
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "fisher", Label = "Fisher",
			Family = MachineFamily.Fisher,
			Tiers = FisherTiers,
			OverlayDir = "block/overlay/machine",
			OverlayBasename = "overlay_screen",
			EmissiveOverlayBasename = "overlay_qtank_emissive",
			LayoutKey = "fisher",
		});

		// === World Accelerator - adapted port of GTMachines.WORLD_ACCELERATOR =
		// Upstream is a per-block random-tick + adjacent BlockEntity accelerator
		// (LV..UV). Adapted: random-tick only; routes each per-tick pick through
		// vanilla's WorldGen.UpdateWorld_{Overground,Underground}Tile dispatcher
		// (reflection - vanilla's own random-update path). Square area side =
		// 2*tier+1. See WorldAcceleratorMachine.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "world_accelerator", Label = "World Accelerator",
			Family = MachineFamily.WorldAccelerator,
			Tiers = WorldAcceleratorTiers,             // LV..UV
			OverlayDir = "block/machines/world_accelerator",
			OverlayBasename = "overlay_front",
			LayoutKey = "world_accelerator",
		});

		// === Block Breaker - adapted port of GTMachines.BLOCK_BREAKER =========
		// Upstream is a front-facing single-block drill (LV..EV). Adapted to
		// Terraria's 2D facing-less world as a dig-down drill: range scales
		// LV=16 -> LuV=512 tiles BELOW the machine; see BlockBreakerMachine.
		// Tier range extended to LuV (vs upstream's EV ceiling) so end-game
		// players can punch through hardmode depths in one machine.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "block_breaker", Label = "Block Breaker",
			Family = MachineFamily.BlockBreaker,
			Tiers = BlockBreakerTiers,                 // LV..ZPM
			OverlayDir = "block/machines/block_breaker",
			OverlayBasename = "overlay_front",
			LayoutKey = "block_breaker",
		});

		// === Miner - adapted port of GTMachines.MINER ========================
		// Upstream is a front-facing single-block ore harvester (LV..HV) that
		// chunks a 3D box downward. Adapted: scan a tier-keyed WxD band
		// (LV 16x500 -> EV 64x2000) directly below the machine. Ore tiles only
		// (TileID.Sets.Ore). Drops route through (tier+1)^2 internal cache +
		// AutoOutput. See MinerMachine.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "miner", Label = "Miner",
			Family = MachineFamily.Miner,
			Tiers = new[] { VoltageTier.LV, VoltageTier.MV, VoltageTier.HV, VoltageTier.EV },
			OverlayDir = "block/machines/miner",
			OverlayBasename = "overlay_front",
			LayoutKey = "miner",
		});

		// === Pump - adapted port of GTMachines.PUMP ==========================
		// Upstream is a front-facing single-block fluid pump (LV..EV) that
		// drains source-fluid blocks in a 3D box. Adapted: scan the same WxD
		// band as Miner, find tiles passing vanilla's bucket-fill gate
		// (target.liquid > 0 && 3x3 sum > 100), drain target + top up from
		// 3x3 neighbours to 255 units = 1000 mB. Two tanks (water + lava),
		// 16 buckets x tier each. See PumpMachine.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "pump", Label = "Pump",
			Family = MachineFamily.Pump,
			Tiers = new[] { VoltageTier.LV, VoltageTier.MV, VoltageTier.HV, VoltageTier.EV },
			// Upstream `pump_machine.json` composites the voltage hull with
			// `overlay_adv_pump` on the front face + a screen on top. Our
			// renderer is single-overlay; pick the pump pictogram for the face.
			OverlayDir = "block/overlay/machine",
			OverlayBasename = "overlay_adv_pump",
			LayoutKey = "pump",
		});

		// === Item Collector - upstream GTMachines.ITEM_COLLECTOR (LV..EV) =====
		// One MachineDefinition row spanning four tiers - inventory size, range,
		// energy draw all derive per-tier inside ItemCollectorMachine. Texture:
		// upstream's per-machine `block/machines/item_collector` overlay.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "item_collector", Label = "Item Collector",
			Family = MachineFamily.ItemCollector,
			Tiers = new[] { VoltageTier.LV, VoltageTier.MV, VoltageTier.HV, VoltageTier.EV },
			OverlayDir = "block/machines/item_collector",
			OverlayBasename = "overlay_top",
			LayoutKey = "item_collector",
		});
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "super_tank", Label = "Super Tank",
			Family = MachineFamily.SuperTank,
			Tiers = SuperTiers,
			OverlayDir = "block/overlay/machine", OverlayBasename = "overlay_qtank",
			LayoutKey = "super_tank",
		});
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "quantum_tank", Label = "Quantum Tank",
			Family = MachineFamily.SuperTank,
			Tiers = QuantumTiers,
			OverlayDir = "block/overlay/machine", OverlayBasename = "overlay_qtank",
			LayoutKey = "super_tank",
		});
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "super_chest", Label = "Super Chest",
			Family = MachineFamily.SuperChest,
			Tiers = SuperTiers,
			OverlayDir = "block/overlay/machine", OverlayBasename = "overlay_qchest",
			LayoutKey = "super_chest",
		});
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "quantum_chest", Label = "Quantum Chest",
			Family = MachineFamily.SuperChest,
			Tiers = QuantumTiers,
			OverlayDir = "block/overlay/machine", OverlayBasename = "overlay_qchest",
			LayoutKey = "super_chest",
		});

		// === Creative storage + energy (debug / testing) ====================
		// 1:1 port of upstream's `creative_chest` / `creative_tank` /
		// `creative_energy_container`. Non-tiered (one of each, pinned at MAX
		// for the purple max-voltage casing - upstream's models all parent off
		// `casings/voltage/max/side`). Overlays:
		//   - creative_chest / _tank -> `overlay_creativecontainer` (the white-pink
		//     "C" sprite from the quantum_container template's screen) +
		//     `overlay_creativecontainer_emissive` (the 14-frame rainbow strip)
		//   - creative_energy -> upstream emits `overlay = void` (transparent) +
		//     `overlay_emissive = overlay_energy_emitter`. Layer it as an
		//     emissive only.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "creative_chest", Label = "Creative Chest",
			Family = MachineFamily.CreativeChest,
			Tiered = false, Tiers = MaxTier,
			OverlayDir = "block/overlay/machine",
			OverlayBasename = "overlay_creativecontainer",
			EmissiveOverlayBasename = "overlay_creativecontainer_emissive",
			LayoutKey = "creative_chest",
		});
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "creative_tank", Label = "Creative Tank",
			Family = MachineFamily.CreativeTank,
			Tiered = false, Tiers = MaxTier,
			OverlayDir = "block/overlay/machine",
			OverlayBasename = "overlay_creativecontainer",
			EmissiveOverlayBasename = "overlay_creativecontainer_emissive",
			LayoutKey = "creative_tank",
		});
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "creative_energy", Label = "Creative Energy Container",
			Family = MachineFamily.CreativeEnergy,
			Tiered = false, Tiers = MaxTier,
			OverlayDir = "block/overlay/machine",
			OverlayBasename = "",
			EmissiveOverlayBasename = "overlay_energy_emitter",
			LayoutKey = "creative_energy",
		});

		// === Coke Oven (primitive multi) - vertical-slice multiblock ========
		// First concrete multi controller wired end-to-end. Pattern: 3x3 with
		// the controller in the centre, 8 cells of coke_oven_bricks casing
		// surrounding (any of the 8 can also be a coke_oven_hatch part).
		// `PatternFactory` is a closure so the casing + hatch tile lookups
		// happen at first-pattern-check time (after `TieredMachineFactory`
		// has registered both tiles).
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "coke_oven", Label = "Coke Oven",
			Family = MachineFamily.MultiblockCokeOven,
			Tiered = false, Tiers = OneTier,
			RecipeType = GTRecipeTypes.COKE_OVEN,
			// Coke brick casing + the multi's front overlay - mirrors
			// upstream `workableCasingModel(machine_coke_bricks,
			// multi/coke_oven)`.
			Casing          = MachineCasing.CokeBricks,
			OverlayDir      = "block/multiblock/coke_oven",
			OverlayBasename = "overlay_front",
			LayoutKey       = "coke_oven",
			PatternFactory  = BuildCokeOvenPattern,
		});

		// === Primitive Blast Furnace (primitive multi) ======================
		// Mirror of GTMultiMachines.java:113-132. 3w x 3d x 4h of firebricks
		// (CASING_PRIMITIVE_BRICKS) with the controller on the front face;
		// 2D-collapsed to the front face's 3x4 in `MultiblockShapes.PrimitiveBlastFurnace`.
		// Recipe type `primitive_blast_furnace` (3 in / 3 out items, 0 fluids).
		// Iron + fuel (coal/charcoal/coke gem|dust|block) + igniter -> steel
		// ingot + dust x 2 (slag / ash).
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "primitive_blast_furnace", Label = "Primitive Blast Furnace",
			Family = MachineFamily.MultiblockPrimitiveBlastFurnace,
			Tiered = false, Tiers = OneTier,
			RecipeType = GTRecipeTypes.PRIMITIVE_BLAST_FURNACE,
			// Firebrick casing + the multi's front overlay - mirrors upstream
			// `workableCasingModel(machine_primitive_bricks,
			// multi/primitive_blast_furnace)`.
			Casing          = MachineCasing.Firebricks,
			OverlayDir      = "block/multiblock/primitive_blast_furnace",
			OverlayBasename = "overlay_front",
			LayoutKey       = "primitive_blast_furnace",
			PatternFactory  = BuildPrimitiveBlastFurnacePattern,
			// Project-local QoL: 10x speed (DurationMultiplier 0.1). Non-
			// upstream - see GTRecipeModifiers.PRIMITIVE_BLAST_FURNACE_SPEEDUP.
			MultiRecipeModifier = GTRecipeModifiers.PRIMITIVE_BLAST_FURNACE_SPEEDUP,
		});

		// === Primitive Pump (primitive multi) ===============================
		// Mirror of GTMultiMachines.PRIMITIVE_PUMP. Biome-keyed water generator -
		// no recipe station, no EU. Production = biomeModifier x hatchModifier,
		// x1.5 when raining. Single PumpHatch part exposes the water output.
		// The pump_hatch part definition is registered first so the pattern's
		// `Predicates.Abilities(PUMP_FLUID_HATCH)` resolves to its tile.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "pump_hatch", Label = "Pump Hatch",
			Family = MachineFamily.PumpHatch,
			Tiered = false, Tiers = new[] { VoltageTier.ULV },
			PartIo = IO.OUT,
			PartFluidSlots = 1,
			PartAbilities = new[] { Api.Machine.Multiblock.PartAbility.PUMP_FLUID_HATCH },
			// Casing = pump_deck/top (planks) + standard output fluid-hatch overlay.
			// Mirrors upstream `block/machine/part/pump_hatch.json`.
			Casing = MachineCasing.PumpDeck,
			OverlayDir = PartOverlayDir,
			OverlayBasename = "overlay_fluid_hatch_output",
			LayoutKey = "fluid_hatch",
		});

		MachineRegistry.Register(new MachineDefinition
		{
			Id = "primitive_pump", Label = "Primitive Water Pump",
			Family = MachineFamily.MultiblockPrimitivePump,
			Tiered = false, Tiers = OneTier,
			// No recipe station - pump produces water through its own tick logic
			// (PrimitivePumpMachine.ProduceWater), not via the RecipeLogic loop.
			RecipeType = GTRecipeTypes.DUMMY,
			Casing          = MachineCasing.PumpDeck,
			OverlayDir      = "block/multiblock/primitive_pump",
			OverlayBasename = "overlay_front",
			LayoutKey       = "primitive_pump",
			PatternFactory  = BuildPrimitivePumpPattern,
		});

		// === Large Boilers (4 tiers) ========================================
		// Mirror of GTMultiMachines.LARGE_BOILER_{BRONZE,STEEL,TITANIUM,
		// TUNGSTENSTEEL} + GTMachineUtils.registerLargeBoiler. Per-tier
		// (casing / pipe / firebox) tile names + (maxTemp / heatSpeed) numbers
		// drive both the pattern factory and the LargeBoilerMachine ticker.
		// `MaxOutputFluidTanks=1` is meaningless here (steam goes to bound
		// output fluid hatches, not the controller); WMM's CapabilitiesFlat
		// walks parts for the actual sink.
		LargeBoiler("bronze",        "Large Bronze Boiler",        "steam_machine_casing",   "bronze_pipe_casing",        "bronze_firebox_casing",        800,  4);
		LargeBoiler("steel",         "Large Steel Boiler",         "solid_machine_casing",   "steel_pipe_casing",         "steel_firebox_casing",        1300,  6);
		LargeBoiler("titanium",      "Large Titanium Boiler",      "stable_machine_casing",  "titanium_pipe_casing",      "titanium_firebox_casing",     2000,  8);
		LargeBoiler("tungstensteel", "Large Tungstensteel Boiler", "robust_machine_casing",  "tungstensteel_pipe_casing", "tungstensteel_firebox_casing", 3000, 12);
		// === Multiblock parts (tiered I/O) ==================================
		// Item buses, fluid hatches, energy hatches - registered as per-(kind x
		// tier) tile + item, with one shared family entity per kind. Mirrors
		// upstream GTMachines.java:605-815. The entity classes (ItemBus /
		// FluidHatch / EnergyHatch) read PartIo / PartAmperage / PartFluidSlots
		// off the definition via `OnDefinitionBound` on fresh placement;
		// LoadData re-runs Configure on reload.

		// Item buses - `(1 + min(9, tier))^2` slots. ALL_TIERS x {IN, OUT}.
		ItemBus("input_bus",  "Input Bus",  IO.IN,  AllTiers);
		ItemBus("output_bus", "Output Bus", IO.OUT, AllTiers);

		// Fluid hatches - three slot densities, IN + OUT. 1x is ALL_TIERS,
		// 4x/9x are MULTI_HATCH_TIERS (EV..UHV) per upstream.
		FluidHatchDef("input_hatch",     "Input Hatch",            IO.IN,  1, AllTiers);
		FluidHatchDef("input_hatch_4x",  "Quadruple Input Hatch",  IO.IN,  4, MultiHatchTiers);
		FluidHatchDef("input_hatch_9x",  "Nonuple Input Hatch",    IO.IN,  9, MultiHatchTiers);
		FluidHatchDef("output_hatch",    "Output Hatch",           IO.OUT, 1, AllTiers);
		FluidHatchDef("output_hatch_4x", "Quadruple Output Hatch", IO.OUT, 4, MultiHatchTiers);
		FluidHatchDef("output_hatch_9x", "Nonuple Output Hatch",   IO.OUT, 9, MultiHatchTiers);

		// Dual hatches - item bus + fluid tank on one face. LuV..MAX x {IN, OUT}.
		// Abilities mirror upstream DUAL_INPUT/OUTPUT_HATCH_ABILITIES:
		// IMPORT_ITEMS + IMPORT_FLUIDS (IN) / EXPORT_ITEMS + EXPORT_FLUIDS (OUT).
		DualHatch("dual_input_hatch",  "Dual Input Hatch",  IO.IN,  DualHatchTiers);
		DualHatch("dual_output_hatch", "Dual Output Hatch", IO.OUT, DualHatchTiers);

		// Energy hatches - 2A standard (ALL_TIERS) + hi-amp 4A/16A/64A
		// (MULTI_HATCH_TIERS). "Dynamo Hatch" is upstream's name for the OUT
		// variant; "Substation Energy Hatch" / "Substation Dynamo Hatch" for
		// the 64A ones.
		EnergyHatchDef("energy_input_hatch",         "Energy Hatch",            IO.IN,  2,  AllTiers);
		EnergyHatchDef("energy_output_hatch",        "Dynamo Hatch",            IO.OUT, 2,  AllTiers);
		EnergyHatchDef("energy_input_hatch_4a",      "4A Energy Hatch",         IO.IN,  4,  MultiHatchTiers);
		EnergyHatchDef("energy_output_hatch_4a",     "4A Dynamo Hatch",         IO.OUT, 4,  MultiHatchTiers);
		EnergyHatchDef("energy_input_hatch_16a",     "16A Energy Hatch",        IO.IN,  16, MultiHatchTiers);
		EnergyHatchDef("energy_output_hatch_16a",    "16A Dynamo Hatch",        IO.OUT, 16, MultiHatchTiers);
		EnergyHatchDef("substation_input_hatch_64a", "64A Substation Energy Hatch", IO.IN,  64, MultiHatchTiers);
		EnergyHatchDef("substation_output_hatch_64a","64A Substation Dynamo Hatch", IO.OUT, 64, MultiHatchTiers);

		// Laser hatches - 6 SKU families = {IN target, OUT source} x {256A, 1024A,
		// 4096A}, IV..MAX tiers. Verbatim with upstream's `registerLaserHatch(io,
		// amperage, ability)` (GTMachineUtils.java:456). Capacity per tier =
		// V[tier] x 64 x amperage. Laser hatches participate in two transport
		// paths: (a) as IEnergyContainer inside a multi (the part-walking
		// OnStructureFormed already picks them up the same way regular energy
		// hatches do); (b) as cross-multi endpoints via the laser pipenet
		// (LaserHatchPartMachine.OnTick pushes through adjacent laser pipes,
		// the walker finds the unique destination).
		LaserHatchDef("256a_laser_target_hatch",  "256A Laser Target Hatch",  IO.IN,  256);
		LaserHatchDef("256a_laser_source_hatch",  "256A Laser Source Hatch",  IO.OUT, 256);
		LaserHatchDef("1024a_laser_target_hatch", "1024A Laser Target Hatch", IO.IN,  1024);
		LaserHatchDef("1024a_laser_source_hatch", "1024A Laser Source Hatch", IO.OUT, 1024);
		LaserHatchDef("4096a_laser_target_hatch", "4096A Laser Target Hatch", IO.IN,  4096);
		LaserHatchDef("4096a_laser_source_hatch", "4096A Laser Source Hatch", IO.OUT, 4096);

		// Rotor holder - single-slot rotor mount for large turbine multis.
		// Upstream `tiersBetween(HV, UV)` -> 6 tiers HV..UV. No GUI yet (the
		// turbine multis themselves aren't ported; the part is registered so
		// crafting recipes producing it resolve and the holder ticks the rotor
		// once a multi binds it). Casing voltage-tinted hull + plain turbine
		// overlay (no pipe / emissive). Material lives on the rotor stack via
		// TurbineRotorItem; the holder just reads it.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "rotor_holder", Label = "Rotor Holder",
			Family = MachineFamily.RotorHolder,
			// Upstream `GTValues.tiersBetween(HV, isHighTier ? OpV : UV)`
			// (GTMachines.java:1070). High-tier range: HV..OpV (11 tiers).
			Tiers = new[]
			{
				VoltageTier.HV,  VoltageTier.EV,  VoltageTier.IV,  VoltageTier.LuV,
				VoltageTier.ZPM, VoltageTier.UV,  VoltageTier.UHV, VoltageTier.UEV,
				VoltageTier.UIV, VoltageTier.UXV, VoltageTier.OpV,
			},
			// Registers each (tier x tile) into PartAbility.ROTOR_HOLDER so
			// Predicates.Abilities(ROTOR_HOLDER) resolves to the rotor holder
			// tiles - required for the LargeTurbine pattern's R cell, which
			// upstream's `ability(PartAbility.ROTOR_HOLDER, ...)` predicate gates on.
			PartAbilities = new[] { Api.Machine.Multiblock.PartAbility.ROTOR_HOLDER },
			// Base sheet: tier hull + `overlay_rotor_holder` - the rotor
			// receptacle frame on the front face is ALWAYS visible. Mirrors
			// upstream's `builder.part(blockModel)` which extends
			// `block/machine/template/rotor_holder/block.json` -> parent
			// `block/overlay/front` with `overlay = overlay_rotor_holder`.
			// Upstream's separate IS_FORMED-gated layer is the 48x48
			// `large_turbine/base_bg + base_ring` background spanning the
			// turbine controller's 3-block face - it belongs on the
			// LargeTurbine controller, not the holder.
			Casing          = MachineCasing.Voltage,
			OverlayDir      = PartOverlayDir,
			OverlayBasename = "overlay_rotor_holder",
			LayoutKey       = "rotor_holder",
		});

		// Parallel hatch - sets the multi's recipe-parallel count (1..4^(tier-EV)).
		// Upstream `GTMachines.PARALLEL_HATCH` (GTMachines.java) - tier-prefixed
		// id `<tier>_parallel_hatch`, tiers IV..UV (max parallel 4 / 16 / 64 / 256).
		// The recipe modifier `GTRecipeModifiers.PARALLEL_HATCH` reads
		// `CurrentParallel` off the controller's cached hatch.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "parallel_hatch", Label = "Parallel Control Hatch",
			Family = MachineFamily.ParallelHatch,
			Tiers = new[] { VoltageTier.IV, VoltageTier.LuV, VoltageTier.ZPM, VoltageTier.UV },
			PartAbilities = new[] { Api.Machine.Multiblock.PartAbility.PARALLEL_HATCH },
			Casing = MachineCasing.Voltage,
			// Per-tier overlay - upstream ships `parallel_hatch_mk{1..4}` and
			// maps IV->mk1, LuV->mk2, ZPM->mk3, UV->mk4. Mirror that here.
			OverlayDirByTier = t =>
				$"block/machines/parallel_hatch_mk{(int)t - (int)VoltageTier.IV + 1}",
			OverlayBasename = "overlay_front",
			LayoutKey = "parallel_hatch",
		});

		// Muffler hatch - recovery-item exhaust. ELECTRIC_TIERS (LV..UV).
		// Upstream model is a single-overlay (no pipe / tinted / emissive).
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "muffler_hatch", Label = "Muffler Hatch",
			Family = MachineFamily.Muffler,
			Tiers = ElectricTiers,
			PartAbilities = new[] { Api.Machine.Multiblock.PartAbility.MUFFLER },
			Casing = MachineCasing.Voltage,
			OverlayDir = PartOverlayDir,
			OverlayBasename = "overlay_muffler",
			LayoutKey = "muffler",
		});

		// === Maintenance hatch family - single-tier each ====================
		// Base maintenance hatch - LV. Single overlay (no emissive).
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "maintenance_hatch", Label = "Maintenance Hatch",
			Family = MachineFamily.MaintenanceHatch,
			Tiered = false, Tiers = new[] { VoltageTier.LV },
			PartConfigurable = false,
			PartAbilities = new[] { Api.Machine.Multiblock.PartAbility.MAINTENANCE },
			Casing = MachineCasing.Voltage,
			OverlayDir = PartOverlayDir,
			OverlayBasename = "overlay_maintenance",
			LayoutKey = "maintenance",
		});
		// Configurable maintenance hatch - HV, duration multiplier UI.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "configurable_maintenance_hatch", Label = "Configurable Maintenance Hatch",
			Family = MachineFamily.MaintenanceHatch,
			Tiered = false, Tiers = new[] { VoltageTier.HV },
			PartConfigurable = true,
			PartAbilities = new[] { Api.Machine.Multiblock.PartAbility.MAINTENANCE },
			Casing = MachineCasing.Voltage,
			OverlayDir = PartOverlayDir,
			OverlayBasename = "overlay_maintenance_configurable",
			LayoutKey = "maintenance",
		});
		// Full-Auto maintenance hatch - HV, always-fixed, no GUI.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "auto_maintenance_hatch", Label = "Full-Auto Maintenance Hatch",
			Family = MachineFamily.AutoMaintenanceHatch,
			Tiered = false, Tiers = new[] { VoltageTier.HV },
			PartAbilities = new[] { Api.Machine.Multiblock.PartAbility.MAINTENANCE },
			Casing = MachineCasing.Voltage,
			OverlayDir = PartOverlayDir,
			OverlayBasename = "overlay_maintenance_full_auto",
			EmissiveOverlayBasename = "overlay_maintenance_full_auto_emissive",
			LayoutKey = "none",
		});
		// Cleaning maintenance hatch - UV, provides CLEANROOM. Full-auto base
		// (no GUI). Sterile variant (UHV) not registered upstream - single
		// upstream id.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "cleaning_maintenance_hatch", Label = "Cleaning Maintenance Hatch",
			Family = MachineFamily.CleaningMaintenanceHatch,
			Tiered = false, Tiers = new[] { VoltageTier.UV },
			PartCleanroomType = Api.Machine.Multiblock.CleanroomType.CLEANROOM,
			PartAbilities = new[] { Api.Machine.Multiblock.PartAbility.MAINTENANCE },
			Casing = MachineCasing.Voltage,
			OverlayDir = PartOverlayDir,
			OverlayBasename = "overlay_maintenance_cleaning",
			EmissiveOverlayBasename = "overlay_maintenance_cleaning_emissive",
			LayoutKey = "none",
		});

		// === Diode (passthrough hatch) - ELECTRIC_TIERS =====================
		// One-way energy valve. Allowed in Cleanroom walls (replaces upstream's
		// `PartAbility.PASSTHROUGH_HATCH` predicate slot - the cleanroom wall
		// predicate resolves diodes by family).
		// Textures: reuse `overlay_energy_1a_in` since the diode starts at 1A.
		// The amp cycle (1/2/4/8/16) mutates gameplay only - visual fidelity
		// gap acknowledged (upstream's per-amp texture swap is non-trivial in
		// our single-overlay path).
		Diode("diode", "Diode");

		// === Steam multi I/O parts ==========================================
		// Mirror of GTMachines.{STEAM_IMPORT_BUS, STEAM_EXPORT_BUS, STEAM_HATCH}.
		// All three are non-tiered (registered once, at LV) - upstream uses
		// `tier = 1` for the buses and `tier = 0` for the steam hatch. The
		// buses reuse the existing `ItemBus` family (same logic - just a
		// different PartAbility token and a bronze-skin casing); the hatch is
		// its own family (`SteamHatchPartMachine` adds the steam-only fluid
		// filter and pins capacity to 64 buckets, regardless of which 1x/4x/9x
		// MachineDefinition variant it's bound to).
		// All three steam parts use the standard 3-layer pipe/hatch composite
		// (pipe back layer -> directional middle -> tinted emissive front), same
		// as regular ItemBus / FluidHatchDef defs. Without all three, the icon
		// renders flat without the pipe outline or the running-emissive accent.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "steam_input_bus", Label = "Steam Input Bus",
			Family = MachineFamily.ItemBus,
			Tiered = false, Tiers = OneTier,
			PartIo = IO.IN,
			PartAbilities = new[] { Api.Machine.Multiblock.PartAbility.STEAM_IMPORT_ITEMS },
			Casing              = MachineCasing.Voltage,
			FusedCasingTileName = "bronze_machine_casing",
			OverlayDir              = PartOverlayDir,
			PipeOverlayBasename     = "overlay_pipe",
			OverlayBasename         = "overlay_item_hatch_input",
			EmissiveOverlayBasename = "overlay_pipe_in_emissive",
			LayoutKey = "item_bus",
		});
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "steam_output_bus", Label = "Steam Output Bus",
			Family = MachineFamily.ItemBus,
			Tiered = false, Tiers = OneTier,
			PartIo = IO.OUT,
			PartAbilities = new[] { Api.Machine.Multiblock.PartAbility.STEAM_EXPORT_ITEMS },
			Casing              = MachineCasing.Voltage,
			FusedCasingTileName = "bronze_machine_casing",
			OverlayDir              = PartOverlayDir,
			PipeOverlayBasename     = "overlay_pipe",
			OverlayBasename         = "overlay_item_hatch_output",
			EmissiveOverlayBasename = "overlay_pipe_out_emissive",
			LayoutKey = "item_bus",
		});
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "steam_input_hatch", Label = "Steam Hatch",
			Family = MachineFamily.SteamHatch,
			Tiered = false, Tiers = new[] { VoltageTier.ULV },
			PartIo = IO.IN,
			PartFluidSlots = 1,
			PartAbilities = new[] { Api.Machine.Multiblock.PartAbility.STEAM },
			Casing              = MachineCasing.Voltage,
			FusedCasingTileName = "bronze_machine_casing",
			OverlayDir              = PartOverlayDir,
			PipeOverlayBasename     = "overlay_pipe",
			OverlayBasename         = "overlay_fluid_hatch_input",
			EmissiveOverlayBasename = "overlay_pipe_in_emissive",
			LayoutKey = "fluid_hatch",
		});

		// === Electric Blast Furnace (EBF) ==================================
		// Fixed 5x5 multi (3-wide coil chamber + perimeter walls). Coil tier
		// gates `ebf_temp` recipes via `EBF_OVERCLOCK`. Controller on bottom
		// center, muffler on top center, hatches in wall cells.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "electric_blast_furnace", Label = "Electric Blast Furnace",
			Family = MachineFamily.MultiblockEBF,
			Tiered = false, Tiers = new[] { VoltageTier.LV },
			RecipeType = GTRecipeTypes.BLAST_RECIPES,
			Casing = MachineCasing.Voltage,
			OverlayDir = "block/multiblock/electric_blast_furnace",
			OverlayBasename = "overlay_front",
			// EBF uses the generic multi layout - its only specific line (heat
			// capacity) lands through `AdditionalDisplay`, matching upstream's
			// `additionalDisplay` lambda at `GTMultiMachines.java:182-192`.
			LayoutKey = "generic_multi",
			AdditionalDisplay   = Multiblock.CoilAdditionalDisplay.BlastFurnaceMaxTemperature,
			PatternFactory = BuildEBFPattern,
			// Bound parts reskin to invar heatproof casing when formed.
			FusedCasingTileName    = "heatproof_machine_casing",
		});

		// === Cleanroom multi controller ====================================
		// Enclosed rectangular room (5-15 wide along the horizontal axis).
		// Walls = plascrete/cleanroom_glass/energy+maintenance hatch tiles;
		// top row = cleanroom_filter casing(s); controller on right wall.
		// Provides CLEANROOM (or STERILE_CLEANROOM if sterilizing_filter_casing
		// is used) to all `CleanroomReceiverTrait`-bearing machines inside.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "cleanroom", Label = "Cleanroom",
			Family = MachineFamily.MultiblockCleanroom,
			Tiered = false, Tiers = new[] { VoltageTier.LV },
			// Cleanroom runs CleanroomLogic (a recipe-less cleanliness ratchet),
			// not actual GTRecipes. Upstream binds it to `DUMMY_RECIPES` so the
			// `GetRecipeType()` lookup that WorkableMultiblockMachine performs
			// during OnStructureFormed has something to return.
			RecipeType = GTRecipeTypes.DUMMY,
			Casing = MachineCasing.Voltage,
			OverlayDir = "block/multiblock/cleanroom",
			OverlayBasename = "overlay_top",
			LayoutKey = "cleanroom",
			PatternFactory = BuildCleanroomPattern,
			// Bound parts reskin to plascrete when formed.
			FusedCasingTileName    = "plascrete",
		});

		// === Large Chemical Reactor (LCR) ===================================
		// Standard processing multi - perfect subtick overclock, no custom
		// modifier. Uses the shared `WorkableElectricMultiblockMachine` entity
		// via `MachineFamily.MultiblockElectricStandard`. Shape: 5x5 with the
		// PTFE pipe at center, coil ring around it, casing/hatch walls.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "large_chemical_reactor", Label = "Large Chemical Reactor",
			Family = MachineFamily.MultiblockElectricStandard,
			Tiered = false, Tiers = new[] { VoltageTier.LV },
			RecipeType = GTRecipeTypes.LARGE_CHEMICAL_RECIPES,
			Casing = MachineCasing.Voltage,
			OverlayDir = "block/multiblock/large_chemical_reactor",
			OverlayBasename = "overlay_front",
			LayoutKey = "generic_multi",
			PatternFactory = BuildLCRPattern,
			// Upstream `.recipeModifiers(DEFAULT_ENVIRONMENT_REQUIREMENT,
			// OC_PERFECT_SUBTICK, BATCH_MODE)`. Environment + batch modes not
			// ported as modifiers yet (env is recipe-condition side, batch is
			// a UI toggle on the multi); the overclock chain is the part that
			// matters for recipe math.
			MultiRecipeModifier = GTRecipeModifiers.OC_PERFECT_SUBTICK,
			// Bound parts reskin to PTFE-inert casing when formed.
			FusedCasingTileName    = "inert_machine_casing",
		});

		// === Vacuum Freezer =====================================================
		// Mirror of GTMultiMachines.java:511-528.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "vacuum_freezer", Label = "Vacuum Freezer",
			Family = MachineFamily.MultiblockElectricStandard,
			Tiered = false, Tiers = OneTier,
			RecipeType = GTRecipeTypes.VACUUM_RECIPES,
			Casing = MachineCasing.Voltage,
			OverlayDir = "block/multiblock/vacuum_freezer",
			OverlayBasename = "overlay_front",
			LayoutKey = "generic_multi",
			PatternFactory = BuildVacuumFreezerPattern,
			MultiRecipeModifier = GTRecipeModifiers.OC_NON_PERFECT_SUBTICK,
			FusedCasingTileName    = "frostproof_machine_casing",
		});

		// === Implosion Compressor ===============================================
		// Mirror of GTMultiMachines.java:263-281.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "implosion_compressor", Label = "Implosion Compressor",
			Family = MachineFamily.MultiblockElectricStandard,
			Tiered = false, Tiers = OneTier,
			RecipeType = GTRecipeTypes.IMPLOSION_RECIPES,
			Casing = MachineCasing.Voltage,
			OverlayDir = "block/multiblock/implosion_compressor",
			OverlayBasename = "overlay_front",
			LayoutKey = "generic_multi",
			PatternFactory = BuildImplosionCompressorPattern,
			MultiRecipeModifier = GTRecipeModifiers.OC_NON_PERFECT_SUBTICK,
			FusedCasingTileName    = "solid_machine_casing",
		});

		// === Large Autoclave ====================================================
		// Mirror of GCYMMachines.java:507-532.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "large_autoclave", Label = "Large Crystallization Chamber",
			Family = MachineFamily.MultiblockElectricStandard,
			Tiered = false, Tiers = OneTier,
			RecipeType = GTRecipeTypes.AUTOCLAVE,
			Casing = MachineCasing.Voltage,
			OverlayDir = "block/multiblock/gcym/large_autoclave",
			OverlayBasename = "overlay_front",
			LayoutKey = "generic_multi",
			PatternFactory = BuildLargeAutoclavePattern,
			MultiRecipeModifier = GTRecipeModifiers.OC_NON_PERFECT_SUBTICK,
			FusedCasingTileName    = "watertight_casing",
		});

		// === Distillation Tower (per-layer fluid output routing) =================
		// Single-input -> N-fluid-output column. Custom controller
		// (DistillationTowerMachine) maps recipe outputs[i] -> hatch at layer i
		// above the controller (`MachineFamily.MultiblockDistillationTower`).
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "distillation_tower", Label = "Distillation Tower",
			Family = MachineFamily.MultiblockDistillationTower,
			Tiered = false, Tiers = OneTier,
			RecipeType = GTRecipeTypes.DISTILLATION_TOWER,
			Casing = MachineCasing.Voltage,
			OverlayDir = "block/multiblock/distillation_tower",
			OverlayBasename = "overlay_front",
			LayoutKey = "generic_multi",
			PatternFactory = BuildDistillationTowerPattern,
			// Upstream `.recipeModifiers(OC_NON_PERFECT_SUBTICK, BATCH_MODE)`.
			// BATCH_MODE deferred (see LCR comment); PARALLEL_HATCH composed
			// ahead by WorkableElectricMultiblockMachine.GetRecipeModifier.
			MultiRecipeModifier = GTRecipeModifiers.OC_NON_PERFECT_SUBTICK,
			FusedCasingTileName = "clean_machine_casing",
		});

		// === Large Distillery (dual-mode: distillation_tower + distillery) =======
		// Shares the DistillationTowerMachine controller; runs both recipe types.
		// Distillery recipes (single fluid output) fall through to the standard
		// dispatcher; distillation_tower recipes use per-layer routing.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "large_distillery", Label = "Large Distillery",
			Family = MachineFamily.MultiblockDistillationTower,
			Tiered = false, Tiers = OneTier,
			RecipeTypes = new[] { GTRecipeTypes.DISTILLATION_TOWER, GTRecipeTypes.DISTILLERY },
			Casing = MachineCasing.Voltage,
			OverlayDir = "block/multiblock/gcym/large_distillery",
			OverlayBasename = "overlay_front",
			LayoutKey = "generic_multi",
			PatternFactory = BuildLargeDistilleryPattern,
			MultiRecipeModifier = GTRecipeModifiers.OC_NON_PERFECT_SUBTICK,
			FusedCasingTileName = "watertight_casing",
		});

		// === Assembly Line (ordered-input multi) ================================
		// Like distillation_tower but horizontal AND ordered on input: the
		// N-th item input goes through the N-th input bus (left-to-right),
		// the N-th fluid input through the N-th input fluid hatch. Custom
		// controller (`AssemblyLineMachine`) overrides
		// `TryMatchInputContents` / `TryConsumeInputContents` to enforce the
		// ordering on top of the standard trie match; outputs + EU go through
		// the standard dispatcher. Upstream's `ConfigHolder.machines.ordered
		// AssemblyLine{Items,Fluids}` defaults to true, dropped here.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "assembly_line", Label = "Assembly Line",
			Family = MachineFamily.MultiblockAssemblyLine,
			Tiered = false, Tiers = OneTier,
			RecipeType = GTRecipeTypes.ASSEMBLY_LINE,
			Casing = MachineCasing.Voltage,
			OverlayDir = "block/multiblock/assembly_line",
			OverlayBasename = "overlay_front",
			LayoutKey = "generic_multi",
			PatternFactory = BuildAssemblyLinePattern,
			// Upstream `.recipeModifiers(DEFAULT_ENVIRONMENT_REQUIREMENT, OC_NON_PERFECT)`.
			// DEFAULT_ENVIRONMENT_REQUIREMENT is the cleanroom-or-hazard modifier
			// (unported environmental hazard subsystem); we keep
			// just OC_NON_PERFECT_SUBTICK as every other electric multi does.
			MultiRecipeModifier = GTRecipeModifiers.OC_NON_PERFECT_SUBTICK,
			FusedCasingTileName = "steel_machine_casing",
		});

		// === Steam parallel multis (steam_grinder + steam_oven) ==================
		// Bespoke `SteamParallelMultiblockMachine` controller. Runs electric-recipe
		// stations (MACERATOR / ELECTRIC_FURNACE) but pays EU as steam through a
		// bound `steam_input_hatch`. Parallels up to MaxParallels per cycle with
		// upstream's (1.5x duration, 8/9 EUt-per-parallel, 32 EUt cap, LV-tier-
		// capped) curve. Family is shared so the bronze hull + steam-themed UI
		// fall out of the family entity - per-multi shape + casing override per
		// def. Pattern factories below resolve `X` / `F` cells.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "steam_grinder", Label = "Steam Grinder",
			Family = MachineFamily.MultiblockSteamParallel,
			Tiered = false, Tiers = OneTier,
			RecipeType = GTRecipeTypes.MACERATOR,
			Casing = MachineCasing.Voltage,
			OverlayDir = "block/multiblock/steam_grinder",
			OverlayBasename = "overlay_front",
			LayoutKey = "steam_parallel_multi",
			PatternFactory = BuildSteamGrinderPattern,
			// Upstream `appearanceBlock(CASING_BRONZE_BRICKS)` - the bronze
			// plated brick walls. Mirrors LargeBoiler's per-tier casing path.
			FusedCasingTileName = "bronze_brick_casing",
		});
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "steam_oven", Label = "Steam Oven",
			Family = MachineFamily.MultiblockSteamParallel,
			Tiered = false, Tiers = OneTier,
			RecipeType = GTRecipeTypes.ELECTRIC_FURNACE,
			Casing = MachineCasing.Voltage,
			OverlayDir = "block/multiblock/steam_oven",
			OverlayBasename = "overlay_front",
			LayoutKey = "steam_parallel_multi",
			PatternFactory = BuildSteamOvenPattern,
			FusedCasingTileName = "bronze_brick_casing",
		});

		// === Tier-4 generator multis (large_combustion_engine, large_*_turbine) =
		// Both use the verbatim upstream `recipeModifier` chains (combustion:
		// lubricant gate + oxygen-boost; turbine: rotor-power-scaled overclock).
		// Each is `IsGenerator = true` so `WorkableElectricMultiblockMachine.
		// GetMaxVoltage()` reads the OUTPUT-side dynamo hatches; the controller
		// pushes EU through `RecipeLogic.DepositOutputEU` -> `HandleEUThroughCap
		// Proxy(IO.OUT)` -> bound dynamo hatches.
		// Tiers={EV/IV/...} carries the controller's tier - the family entities
		// read `MetaMachine.Tier` to drive `V[tier]` math.
		LargeCombustionEngine("large_combustion_engine",  "Large Combustion Engine",       (int)VoltageTier.EV,
			"stable_machine_casing", "titanium_gearbox",     "engine_intake_casing",
			"block/multiblock/generator/large_combustion_engine");
		LargeCombustionEngine("extreme_combustion_engine","Extreme Combustion Engine",     (int)VoltageTier.IV,
			"robust_machine_casing", "tungstensteel_gearbox", "extreme_engine_intake_casing",
			"block/multiblock/generator/extreme_combustion_engine");

		// Large turbines - `RecipeType` matches upstream's `STEAM_TURBINE_FUELS /
		// GAS_TURBINE_FUELS / PLASMA_GENERATOR_FUELS` (we reuse the existing
		// singleblock STEAM_TURBINE / GAS_TURBINE recipe types - they're the same
		// `<station>` id upstream).
		// Upstream ids are `<fuel>_large_turbine` (NOT `large_<fuel>_turbine`) -
		// recipes + the item dump + the locale all key off that form, so the
		// controller item only resolves in recipes when the MachineKey matches.
		// (The texture folders ARE named `large_<fuel>_turbine`; that's an
		// independent asset path passed as overlayDir, left as-is.)
		LargeTurbine("steam_large_turbine",  "Large Steam Turbine",  VoltageTier.HV,
			GTRecipeTypes.STEAM_TURBINE,    "steel_turbine_casing",          "steel_gearbox",
			"block/multiblock/generator/large_steam_turbine",  needsMuffler: false);
		LargeTurbine("gas_large_turbine",    "Large Gas Turbine",    VoltageTier.EV,
			GTRecipeTypes.GAS_TURBINE,      "stainless_steel_turbine_casing","stainless_steel_gearbox",
			"block/multiblock/generator/large_gas_turbine",    needsMuffler: true);
		LargeTurbine("plasma_large_turbine", "Large Plasma Turbine", VoltageTier.IV,
			GTRecipeTypes.PLASMA_GENERATOR, "tungstensteel_turbine_casing",  "tungstensteel_gearbox",
			"block/multiblock/generator/large_plasma_turbine", needsMuffler: false);

		// === Tier-4 world-I/O multis (large_miner, fluid_drilling_rig) ==========
		// Adapted from upstream: instead of mining real 3D ore blocks / depleting
		// per-chunk fluid veins, we run a biome-keyed lottery - see
		// `Multiblock.Electric.BiomeWorldIOTables` for the per-biome ore pools and
		// the per-biome fluid pick. No depletion; both produce while powered + IO-
		// bound. Casings + frames mirror upstream's `getCasingState`/`getMaterial`
		// per tier (Steel / Titanium / TungstenSteel).
		//
		// `RecipeType = DUMMY` keeps RecipeLogic dormant but attached - the
		// IsWorkingEnabled flag still persists across world saves, and the recipe
		// browser doesn't see these machines (DUMMY has no recipes; the coverage
		// check exempts it).
		// Per-tier ids mirror upstream's `<tier>_<id>` naming from
		// `registerTieredMultis("large_miner", ..., EV, IV, LuV)` - `ev_large_miner`,
		// `iv_large_miner`, `luv_large_miner`. One def per tier (`Tiered=false`,
		// `Tiers={one}`) so each tier carries its own casing + frame material;
		// the upstream-style tier prefix keeps registry lookups identical to
		// upstream item ids.
		LargeMiner("ev_large_miner",  "Large Miner",  VoltageTier.EV,
			"solid_machine_casing",   "steel");
		LargeMiner("iv_large_miner",  "Advanced Large Miner",  VoltageTier.IV,
			"stable_machine_casing",  "titanium");
		LargeMiner("luv_large_miner", "Advanced Large Miner II", VoltageTier.LuV,
			"robust_machine_casing",  "tungsten_steel");

		FluidDrillingRig("mv_fluid_drilling_rig", "Fluid Drilling Rig",          VoltageTier.MV,
			"solid_machine_casing",   "steel");
		FluidDrillingRig("hv_fluid_drilling_rig", "Advanced Fluid Drilling Rig", VoltageTier.HV,
			"stable_machine_casing",  "titanium");
		FluidDrillingRig("ev_fluid_drilling_rig", "Advanced Fluid Drilling Rig II", VoltageTier.EV,
			"robust_machine_casing",  "tungsten_steel");

		// === Fusion Reactor (3 tiers - LuV / ZPM / UV) ==========================
		// Verbatim upstream - bespoke `FusionReactorMachine` controller owns its
		// own NotifiableEnergyContainer (capacitor) + `_heat` field; FUSION_OC
		// modifier in GTRecipeModifiers pays `eu_to_start` from the capacitor.
		// Casing + coil tile per tier exactly as upstream `getCasingState` /
		// `getCoilState` (Mk1=fusion_casing, Mk2/Mk3 = fusion_casing_mk{2,3};
		// LuV coil = superconducting_coil, ZPM/UV coil = fusion_coil).
		FusionReactor("luv_fusion_reactor", "Fusion Reactor Mk I",   VoltageTier.LuV,
			"fusion_casing",      "superconducting_coil");
		FusionReactor("zpm_fusion_reactor", "Fusion Reactor Mk II",  VoltageTier.ZPM,
			"fusion_casing_mk2",  "fusion_coil");
		FusionReactor("uv_fusion_reactor",  "Fusion Reactor Mk III", VoltageTier.UV,
			"fusion_casing_mk3",  "fusion_coil");

		// === Active transformer (power converter) ==============================
		// 1:1 port of upstream `GTMultiMachines.ACTIVE_TRANSFORMER` (line 955).
		// Bound input energy hatches feed bound output energy hatches each
		// tick; no recipes. Structural failure while running explodes at
		// `6 + tier` via `EnvironmentalExplosionTrait.DoExplosionAt` -
		// verbatim upstream default (project policy). Single
		// UV SKU (upstream registers no static tier; we pick UV per the
		// generator-multi convention so `getTier()` is deterministic from
		// the placed item).
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "active_transformer", Label = "Active Transformer",
			Family = MachineFamily.MultiblockActiveTransformer,
			Tiered = false, Tiers = new[] { VoltageTier.UV },
			// Upstream `.recipeType(DUMMY_RECIPES)` - the multi has its own
			// per-tick conversion loop, no recipe matching.
			RecipeType = GTRecipeTypes.DUMMY,
			Casing = MachineCasing.Voltage,
			// Upstream `.workableCasingModel(HIGH_POWER_CASING, data_bank)` -
			// the front-face overlay re-uses the data_bank art since active
			// transformer doesn't ship a dedicated overlay (verbatim with
			// GTMultiMachines.java:976-977).
			OverlayDir = "block/multiblock/data_bank", OverlayBasename = "overlay_front",
			LayoutKey = "generic_multi",
			PatternFactory = () => BuildActiveTransformerPattern(),
			// Upstream `.appearanceBlock(HIGH_POWER_CASING)` - controller's
			// own block face + fused-casing reskin both pull from this.
			FusedCasingTileName = "high_power_casing",
		});

		// === Power Substation (bulk EU storage) ================================
		// 1:1 port of upstream `GTMultiMachines.POWER_SUBSTATION`
		// (GTMultiMachines.java:980-1041). Bespoke controller (`PowerSubstation
		// Machine`) banks EU from input hatches into a PowerStationEnergyBank
		// trait (sum of installed battery capacities), debanks to output
		// hatches every 20 ticks with a small passive drain off the top.
		// Single UV SKU - structure invariant gates on at least 1 filled
		// battery + 1 input hatch + 1 output hatch.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "power_substation", Label = "Power Substation",
			Family = MachineFamily.MultiblockPowerSubstation,
			Tiered = false, Tiers = new[] { VoltageTier.UV },
			RecipeType = GTRecipeTypes.DUMMY,
			Casing = MachineCasing.Voltage,
			// Upstream uses `appearanceBlock(CASING_PALLADIUM_SUBSTATION)`
			// with no overlay model factory call - the controller renders as
			// the casing texture. We hand the same casing through
			// `FusedCasingTileName` so the controller cell + bound parts both
			// reskin to it.
			// Upstream models the controller as `appearanceBlock(CASING_PALLADIUM_SUBSTATION)`
			// plus the dedicated front-face textures shipped under
			// `block/multiblock/power_substation/overlay_front{,_active}{,_emissive}.png`.
			// The casing is supplied via FusedCasingTileName below; this is
			// the layered overlay (directional + emissive when active).
			OverlayDir = "block/multiblock/power_substation",
			OverlayBasename         = "overlay_front",
			EmissiveOverlayBasename = "overlay_front_emissive",
			// In-GUI display panel = port of upstream createUIWidget's
			// ComponentPanelWidget(addDisplayText). Substation extends
			// WorkableMultiblockMachine directly (NOT WEMM - doesn't use
			// overclock / _energyContainer aggregation), so it can't reuse the
			// "generic_multi" layout (whose getter casts to WEMM); it gets a
			// dedicated single-panel layout instead. The same content also
			// surfaces on world hover via AppendTooltip.
			LayoutKey = "power_substation",
			PatternFactory = () => BuildPowerSubstationPattern(),
			FusedCasingTileName = "palladium_substation",
		});

		// === Research / computation subsystem ==================================
		// 1:1 port of GTResearchMachines.java. Controllers are DUMMY-recipe
		// passive multis (HPCA / DataBank / NetworkSwitch) + the recipe-driven
		// ResearchStation. Appearance casings per upstream `appearanceBlock`.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "high_performance_computation_array", Label = "High Performance Computing Array",
			Family = MachineFamily.MultiblockHPCA,
			Tiered = false, Tiers = new[] { VoltageTier.ZPM },
			RecipeType = GTRecipeTypes.DUMMY,
			Casing = MachineCasing.Voltage,
			FusedCasingTileName = "computer_casing",
			OverlayDir = "block/multiblock/hpca", OverlayBasename = "overlay_front",
			LayoutKey = "research_provider",
			AdditionalDisplay = Multiblock.HpcaAdditionalDisplay.HpcaInfo,
			PatternFactory = () => BuildHpcaPattern(),
		});
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "data_bank", Label = "Data Bank",
			Family = MachineFamily.MultiblockDataBank,
			Tiered = false, Tiers = new[] { VoltageTier.ZPM },
			RecipeType = GTRecipeTypes.DUMMY,
			Casing = MachineCasing.Voltage,
			FusedCasingTileName = "computer_casing",
			OverlayDir = "block/multiblock/data_bank", OverlayBasename = "overlay_front",
			LayoutKey = "research_provider",
			AdditionalDisplay = Multiblock.HpcaAdditionalDisplay.DataBankInfo,
			PatternFactory = () => BuildDataBankPattern(),
		});
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "network_switch", Label = "Network Switch",
			Family = MachineFamily.MultiblockNetworkSwitch,
			Tiered = false, Tiers = new[] { VoltageTier.ZPM },
			RecipeType = GTRecipeTypes.DUMMY,
			Casing = MachineCasing.Voltage,
			FusedCasingTileName = "computer_casing",
			OverlayDir = "block/multiblock/network_switch", OverlayBasename = "overlay_front",
			LayoutKey = "research_provider",
			AdditionalDisplay = Multiblock.HpcaAdditionalDisplay.NetworkSwitchComputation,
			PatternFactory = () => BuildNetworkSwitchPattern(),
		});
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "research_station", Label = "Research Station",
			Family = MachineFamily.MultiblockResearchStation,
			Tiered = false, Tiers = new[] { VoltageTier.ZPM },
			RecipeType = GTRecipeTypes.RESEARCH_STATION,
			Casing = MachineCasing.Voltage,
			FusedCasingTileName = "advanced_computer_casing",
			OverlayDir = "block/multiblock/research_station", OverlayBasename = "overlay_front",
			// Dedicated layout - upstream ResearchStationMachine.addDisplayText is a
			// CUSTOM override ("Researching" keys + percent progress + the
			// computation line), not the generic WEMM recipe body.
			LayoutKey = "research_station",
			PatternFactory = () => BuildResearchStationPattern(),
		});

		// --- Research parts ---------------------------------------------------
		// Object holder (ZPM) - research station pedestal.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = "object_holder", Label = "Object Holder",
			Family = MachineFamily.ObjectHolder,
			Tiered = false, Tiers = new[] { VoltageTier.ZPM },
			PartAbilities = new[] { Api.Machine.Multiblock.PartAbility.OBJECT_HOLDER },
			Casing = MachineCasing.Voltage,
			OverlayDir = "block/machines/object_holder", OverlayBasename = "overlay_front",
			LayoutKey = "object_holder",
		});

		// HPCA grid components (ZPM) - one collapsed entity per kind.
		// Upstream models block/machine/part/hpca_*_component.json render a
		// computer_casing base + a block/overlay/machine/hpca/<type> overlay
		// (active variants auto-loaded via the IsActive flag). The base is
		// reskinned to computer_casing via FusedCasingTileName.
		void Hpca(string id, string label, Multiblock.Part.Hpca.HpcaComponentKind kind, string overlay) =>
			MachineRegistry.Register(new MachineDefinition
			{
				Id = id, Label = label,
				Family = MachineFamily.HpcaComponent,
				Tiered = false, Tiers = new[] { VoltageTier.ZPM },
				PartAbilities = new[] { Api.Machine.Multiblock.PartAbility.HPCA_COMPONENT },
				HpcaKind = kind,
				Casing = MachineCasing.Voltage,
				FusedCasingTileName = "computer_casing",
				OverlayDir = "block/overlay/machine/hpca", OverlayBasename = overlay,
				LayoutKey = "none",
			});
		Hpca("hpca_empty_component",                "HPCA Empty Component",                Multiblock.Part.Hpca.HpcaComponentKind.Empty,               "empty");
		Hpca("hpca_computation_component",          "HPCA Computation Component",          Multiblock.Part.Hpca.HpcaComponentKind.Computation,         "computation");
		Hpca("hpca_advanced_computation_component", "HPCA Advanced Computation Component", Multiblock.Part.Hpca.HpcaComponentKind.AdvancedComputation, "advanced_computation");
		Hpca("hpca_heat_sink_component",            "HPCA Heat Sink Component",            Multiblock.Part.Hpca.HpcaComponentKind.HeatSink,           "heat_sink");
		Hpca("hpca_active_cooler_component",        "HPCA Active Cooler Component",        Multiblock.Part.Hpca.HpcaComponentKind.ActiveCooler,       "active_cooler");
		Hpca("hpca_bridge_component",               "HPCA Bridge Component",               Multiblock.Part.Hpca.HpcaComponentKind.Bridge,             "bridge");

		// Data access hatches (HV/EV/LuV/MAX) - research-data store + recipe gate.
		void DataAccess(string id, string label, VoltageTier tier, bool creative) =>
			MachineRegistry.Register(new MachineDefinition
			{
				Id = id, Label = label,
				Family = MachineFamily.DataAccessHatch,
				Tiered = false, Tiers = new[] { tier },
				DataAccessCreative = creative,
				PartAbilities = new[] { Api.Machine.Multiblock.PartAbility.DATA_ACCESS },
				Casing = MachineCasing.Voltage,
				// Upstream model: block/machine/part/data_access_hatch{,_creative}.json
				// -> overlay_data_hatch{,_creative} (+ emissive) at block/overlay/machine.
				OverlayDir = PartOverlayDir,
				OverlayBasename         = creative ? "overlay_data_hatch_creative"          : "overlay_data_hatch",
				EmissiveOverlayBasename = creative ? "overlay_data_hatch_creative_emissive" : "overlay_data_hatch_emissive",
				LayoutKey = "data_access",
			});
		DataAccess("basic_data_access_hatch",    "Basic Data Access Hatch",    VoltageTier.HV,  false);
		DataAccess("data_access_hatch",          "Data Access Hatch",          VoltageTier.EV,  false);
		DataAccess("advanced_data_access_hatch", "Advanced Data Access Hatch", VoltageTier.LuV, false);
		DataAccess("creative_data_access_hatch", "Creative Data Access Hatch", VoltageTier.MAX, true);

		// Optical computation hatches (ZPM) - CWU transmitter / receiver.
		void CompHatch(string id, string label, bool transmitter, Api.Machine.Multiblock.PartAbility ability) =>
			MachineRegistry.Register(new MachineDefinition
			{
				Id = id, Label = label,
				Family = MachineFamily.OpticalComputationHatch,
				Tiered = false, Tiers = new[] { VoltageTier.ZPM },
				OpticalTransmitter = transmitter,
				PartAbilities = new[] { ability },
				Casing = MachineCasing.Voltage,
				// Upstream model computation_data_hatch.json -> overlay_data_hatch_optical
				// (shared with the optical data hatches).
				OverlayDir = PartOverlayDir,
				OverlayBasename         = "overlay_data_hatch_optical",
				EmissiveOverlayBasename = "overlay_data_hatch_optical_emissive",
				LayoutKey = "none",
			});
		CompHatch("computation_transmitter_hatch", "Computation Data Transmission Hatch", true,  Api.Machine.Multiblock.PartAbility.COMPUTATION_DATA_TRANSMISSION);
		CompHatch("computation_receiver_hatch",    "Computation Data Reception Hatch",    false, Api.Machine.Multiblock.PartAbility.COMPUTATION_DATA_RECEPTION);

		// Optical data hatches (LuV) - research-data transmitter / receiver.
		void DataHatch(string id, string label, bool transmitter, Api.Machine.Multiblock.PartAbility ability) =>
			MachineRegistry.Register(new MachineDefinition
			{
				Id = id, Label = label,
				Family = MachineFamily.OpticalDataHatch,
				Tiered = false, Tiers = new[] { VoltageTier.LuV },
				OpticalTransmitter = transmitter,
				PartAbilities = new[] { ability },
				Casing = MachineCasing.Voltage,
				// Upstream model optical_data_hatch.json -> overlay_data_hatch_optical.
				OverlayDir = PartOverlayDir,
				OverlayBasename         = "overlay_data_hatch_optical",
				EmissiveOverlayBasename = "overlay_data_hatch_optical_emissive",
				LayoutKey = "none",
			});
		DataHatch("data_transmitter_hatch", "Optical Data Transmission Hatch", true,  Api.Machine.Multiblock.PartAbility.OPTICAL_DATA_TRANSMISSION);
		DataHatch("data_receiver_hatch",    "Optical Data Reception Hatch",    false, Api.Machine.Multiblock.PartAbility.OPTICAL_DATA_RECEPTION);

		// === Bulk-ported standard processing multis ============================
		// Each is a `MachineDefinition` row mirroring upstream's `.multiblock(...)`
		// chain in `GTMultiMachines.java` / `GCYMMachines.java`. They share the
		// `WorkableElectricMultiblockMachine` entity, the `generic_multi` layout,
		// and the `OC_NON_PERFECT_SUBTICK` recipe modifier (upstream's
		// `PARALLEL_HATCH` modifier is unported - collapses to identity until
		// the parallel-hatch UI lands; the parallel-hatch part already exists
		// inert).
		Std("large_centrifuge",        "Large Centrifugal Unit",      BuildLargeCentrifugePattern,        "vibration_safe_casing",            "block/multiblock/gcym/large_centrifuge",        GTRecipeTypes.CENTRIFUGE, GTRecipeTypes.THERMAL_CENTRIFUGE);
		Std("large_electrolyzer",      "Large Electrolysis Chamber",  BuildLargeElectrolyzerPattern,      "nonconducting_casing",             "block/multiblock/gcym/large_electrolyzer",      GTRecipeTypes.ELECTROLYZER);
		// Upstream reuses large_electrolyzer's overlay for the electromagnet
		// (GCYMMachines.java:251) - no dedicated `large_electromagnet/` dir.
		Std("large_electromagnet",     "Large Electromagnet",         BuildLargeElectromagnetPattern,     "nonconducting_casing",             "block/multiblock/gcym/large_electrolyzer",      GTRecipeTypes.ELECTROMAGNETIC_SEPARATOR, GTRecipeTypes.POLARIZER);
		Std("large_packer",            "Large Packaging Machine",     BuildLargePackerPattern,            "robust_machine_casing",            "block/multiblock/gcym/large_packer",            GTRecipeTypes.PACKER);
		Std("large_assembler",         "Large Assembling Factory",    BuildLargeAssemblerPattern,         "large_scale_assembler_casing",     "block/multiblock/gcym/large_assembler",         GTRecipeTypes.ASSEMBLER);
		Std("large_circuit_assembler", "Large Circuit Assembling Facility", BuildLargeCircuitAssemblerPattern, "large_scale_assembler_casing", "block/multiblock/gcym/large_circuit_assembler", GTRecipeTypes.CIRCUIT_ASSEMBLER);
		Std("large_arc_smelter",       "Large Arc Smelter",           BuildLargeArcSmelterPattern,        "high_temperature_smelting_casing", "block/multiblock/gcym/large_arc_smelter",       GTRecipeTypes.ARC_FURNACE);
		Std("large_engraving_laser",   "Large Engraving Laser",       BuildLargeEngravingLaserPattern,    "laser_safe_engraving_casing",      "block/multiblock/gcym/large_engraving_laser",   GTRecipeTypes.LASER_ENGRAVER);
		Std("large_sifting_funnel",    "Large Sifting Funnel",        BuildLargeSiftingFunnelPattern,     "vibration_safe_casing",            "block/multiblock/gcym/large_sifting_funnel",    GTRecipeTypes.SIFTER);
		Std("large_material_press",    "Large Material Press",        BuildLargeMaterialPressPattern,     "stress_proof_casing",              "block/multiblock/gcym/large_material_press",    GTRecipeTypes.BENDER, GTRecipeTypes.COMPRESSOR, GTRecipeTypes.FORGE_HAMMER, GTRecipeTypes.FORMING_PRESS);
		Std("large_brewer",            "Large Brewing Vat",           BuildLargeBrewerPattern,            "corrosion_proof_casing",           "block/multiblock/gcym/large_brewer",            GTRecipeTypes.BREWERY, GTRecipeTypes.FERMENTER, GTRecipeTypes.FLUID_HEATER);
		Std("large_cutter",            "Large Cutting Saw",           BuildLargeCutterPattern,            "shock_proof_cutting_casing",       "block/multiblock/gcym/large_cutter",            GTRecipeTypes.CUTTER, GTRecipeTypes.LATHE);
		Std("large_extractor",         "Large Extraction Machine",    BuildLargeExtractorPattern,         "watertight_casing",                "block/multiblock/gcym/large_extractor",         GTRecipeTypes.EXTRACTOR, GTRecipeTypes.CANNER);
		Std("large_extruder",          "Large Extrusion Machine",     BuildLargeExtruderPattern,          "stress_proof_casing",              "block/multiblock/gcym/large_extruder",          GTRecipeTypes.EXTRUDER);
		Std("large_solidifier",        "Large Solidification Array",  BuildLargeSolidifierPattern,        "watertight_casing",                "block/multiblock/gcym/large_solidifier",        GTRecipeTypes.FLUID_SOLIDIFIER);
		Std("large_wiremill",          "Large Wire Factory",          BuildLargeWiremillPattern,          "stress_proof_casing",              "block/multiblock/gcym/large_wiremill",          GTRecipeTypes.WIREMILL);
		// Upstream LargeChemicalBath / LargeMixer each attach a
		// MultiblockFluidRendererTrait - an in-world 3D render of the input-hatch
		// fluid inside the structure's cavity. Deliberately omitted: Terraria is
		// 2D, the trait has no analogue. LargeMacerationTower's entity-grind +
		// item-suck zone (LargeMacerationTowerMachine.spinWheels) is also
		// omitted - recipe-driven behavior stays identical without it. Both are
		// `Std(...)` rows with the controller bound to the shared
		// `WorkableElectricMultiblockMachine` family entity, like every other
		// standard GCYM multi.
		Std("large_chemical_bath",     "Large Chemical Bath",         BuildLargeChemicalBathPattern,      "watertight_casing",                "block/multiblock/gcym/large_chemical_bath",     GTRecipeTypes.CHEMICAL_BATH, GTRecipeTypes.ORE_WASHER);
		Std("large_maceration_tower",  "Large Maceration Tower",      BuildLargeMacerationTowerPattern,   "secure_maceration_casing",         "block/multiblock/gcym/large_maceration_tower",  GTRecipeTypes.MACERATOR);
		Std("large_mixer",             "Large Mixing Vessel",         BuildLargeMixerPattern,             "reaction_safe_mixing_casing",      "block/multiblock/gcym/large_mixer",             GTRecipeTypes.MIXER);
		Std("mega_vacuum_freezer",     "Bulk Blast Chiller",          BuildMegaVacuumFreezerPattern,      "frostproof_machine_casing",        "block/multiblock/gcym/mega_vacuum_freezer",     GTRecipeTypes.VACUUM_RECIPES);

		// === Tier-2: coil-based multis =========================================
		// Share `CoilWorkableElectricMultiblockMachine` (no per-controller UI).
		// Each row supplies its own coil-aware modifier - multiSmelterParallel /
		// pyrolyseOvenOverclock / crackerOverclock / ebfOverclock.
		// multi_smelter is the only multi-mode multi (FURNACE + ALLOY_SMELTER cycling).
		// Upstream's multi_smelter uses `block/multiblock/multi_furnace` as its
		// overlay dir (GTMultiMachines.java) - keep the upstream filename.
		// Each row's `additionalDisplay` arg is the verbatim port of upstream's
		// `.additionalDisplay((controller, components) -> { ... })` lambda for
		// that multi (see `CoilAdditionalDisplay` for the upstream line refs).
		Coil("multi_smelter",        "Multi Smelter",               BuildMultiSmelterPattern,       "heatproof_machine_casing",
			"block/multiblock/multi_furnace",           GTRecipeModifiers.MULTI_SMELTER_PARALLEL,
			Multiblock.CoilAdditionalDisplay.MultiSmelterCoilStats,
			GTRecipeTypes.ELECTRIC_FURNACE, GTRecipeTypes.ALLOY_SMELTER);
		Coil("pyrolyse_oven",        "Pyrolyse Oven",               BuildPyrolyseOvenPattern,       "ulv_machine_casing",
			"block/multiblock/pyrolyse_oven",           GTRecipeModifiers.PYROLYSE_OVERCLOCK,
			Multiblock.CoilAdditionalDisplay.PyrolyseOvenSpeed,
			GTRecipeTypes.PYROLYSE_RECIPES);
		Coil("cracker",              "Cracking Unit",               BuildCrackerPattern,            "clean_machine_casing",
			"block/multiblock/cracking_unit",           GTRecipeModifiers.CRACKER_OVERCLOCK,
			Multiblock.CoilAdditionalDisplay.CrackingUnitEnergy,
			GTRecipeTypes.CRACKING_RECIPES);
		Coil("alloy_blast_smelter",  "Alloy Blast Smelter",         BuildAlloyBlastSmelterPattern,  "high_temperature_smelting_casing",
			"block/multiblock/gcym/blast_alloy_smelter", GTRecipeModifiers.EBF_OVERCLOCK,
			Multiblock.CoilAdditionalDisplay.BlastFurnaceMaxTemperature,
			GTRecipeTypes.ALLOY_BLAST_RECIPES);
		Coil("mega_blast_furnace",   "Rotary Hearth Furnace",       BuildMegaBlastFurnacePattern,   "high_temperature_smelting_casing",
			"block/multiblock/gcym/mega_blast_furnace", GTRecipeModifiers.EBF_OVERCLOCK,
			Multiblock.CoilAdditionalDisplay.BlastFurnaceMaxTemperature,
			GTRecipeTypes.BLAST_RECIPES);

		MachineRegistry.Register(new MachineDefinition
		{
			Id = "coke_oven_hatch", Label = "Coke Oven Hatch",
			Family = MachineFamily.CokeOvenHatch,
			Tiered = false, Tiers = OneTier,
			// Hatch shares the coke brick casing + the front-face hatch
			// overlay - verbatim upstream `block/machine/part/coke_oven_hatch`
			// model, whose `#overlay` texture is `overlay/machine/overlay_hatch`.
			Casing          = MachineCasing.CokeBricks,
			OverlayDir      = "block/overlay/machine",
			OverlayBasename = "overlay_hatch",
			LayoutKey       = "none",
		});

		// === Multiblock tanks (3 storage controllers + 3 valve parts) =========
		// Mirror of GTMultiMachines.{WOODEN,BRONZE,STEEL}_MULTIBLOCK_TANK +
		// GTMachineUtils.registerMultiblockTank/registerTankValve. Each tier is
		// a 3x3 hollow shell of wall casing with the controller on the front
		// face; up to 2 cells may be replaced by the matching tank-valve part.
		// Storage is a single `NotifiableFluidTank(1, capacity, IO.BOTH)` on
		// the controller; the valve exposes it to neighbours via a
		// `FluidTankProxyTrait`. Per-material containment filter is resolved
		// through `MaterialId` -> `MaterialRegistry.Get(...).FluidPipe` (same
		// path drums use); steel has no MaterialId -> no filter, matching
		// upstream's `null` PropertyFluidFilter arg.
		// Valves are registered FIRST so the controller's pattern factory can
		// look up their tile names.
		MultiblockTankValve("wooden_tank_valve", "Wooden Tank Valve", "wood_wall");
		MultiblockTankValve("bronze_tank_valve", "Bronze Tank Valve", "bronze_brick_casing");
		MultiblockTankValve("steel_tank_valve",  "Steel Tank Valve",  "steel_machine_casing");
		MultiblockTank("wooden_multiblock_tank", "Wooden Multiblock Tank", "wood",   250_000,   "wood_wall",            "wooden_tank_valve");
		MultiblockTank("bronze_multiblock_tank", "Bronze Multiblock Tank", "bronze", 500_000,   "bronze_brick_casing",  "bronze_tank_valve");
		MultiblockTank("steel_multiblock_tank",  "Steel Multiblock Tank",  null,     1_000_000, "steel_machine_casing", "steel_tank_valve");

		// === Drums - per-material fluid storage (verbatim GTMachines) ========
		// Capacity = upstream buckets x 1000 mB. Non-tiered: id is `<mat>_drum`.
		Drum("wood_drum",            "Wooden Drum",           "wood",             16_000);
		Drum("bronze_drum",          "Bronze Drum",           "bronze",           32_000);
		Drum("steel_drum",           "Steel Drum",            "steel",            64_000);
		Drum("aluminium_drum",       "Aluminium Drum",        "aluminium",       128_000);
		Drum("stainless_steel_drum", "Stainless Steel Drum",  "stainless_steel", 256_000);
		Drum("gold_drum",            "Gold Drum",             "gold",             32_000);
		Drum("titanium_drum",        "Titanium Drum",         "titanium",        512_000);
		Drum("tungsten_steel_drum",  "Tungstensteel Drum",    "tungsten_steel", 1024_000);

		// === Crates - per-material item storage (verbatim GTMachines) ========
		// Capacity = inventory slot count. Non-tiered: id is `<mat>_crate`.
		Crate("wood_crate",            "Wooden Crate",          "wood",             27);
		Crate("bronze_crate",          "Bronze Crate",          "bronze",           54);
		Crate("steel_crate",           "Steel Crate",           "steel",            72);
		Crate("aluminium_crate",       "Aluminium Crate",       "aluminium",        90);
		Crate("stainless_steel_crate", "Stainless Steel Crate", "stainless_steel", 108);
		Crate("titanium_crate",        "Titanium Crate",        "titanium",        126);
		Crate("tungsten_steel_crate",  "Tungstensteel Crate",   "tungsten_steel",  144);
	}

	// One crate - a per-material item-storage machine (MachineFamily.Crate).
	// Non-tiered: a single placeholder tier, id == the bare upstream id.
	private static void Crate(string id, string label, string materialId, int slotCount)
	{
		MachineRegistry.Register(new MachineDefinition
		{
			Id = id, Label = label,
			Family = MachineFamily.Crate,
			Tiered = false,
			Tiers = OneTier,
			MaterialId = materialId,
			Capacity = slotCount,
			Casing = MachineCasing.None,
			LayoutKey = "crate",
		});
	}

	// One drum - a per-material fluid-storage machine (MachineFamily.Drum).
	// Non-tiered: a single placeholder tier, id == the bare upstream id.
	private static void Drum(string id, string label, string materialId, int capacity)
	{
		MachineRegistry.Register(new MachineDefinition
		{
			Id = id, Label = label,
			Family = MachineFamily.Drum,
			Tiered = false,
			Tiers = OneTier,
			MaterialId = materialId,
			Capacity = capacity,
			Casing = MachineCasing.None,
			LayoutKey = "drum",
		});
	}

	// One item-bus row - input_bus / output_bus x all tiers. The universal
	// tile/item resolves Texture from OverlayDir/OverlayBasename; upstream's
	// `colorOverlayTieredHullModel(OVERLAY_ITEM_HATCH_{IN,OUT}, overlay_pipe,
	// ...)` composites a tier-colored hull + overlay_pipe + overlay_pipe_{in,out}.
	// Renderer composition is per-part fidelity; we use the upstream overlay
	// PNG directly - `block/part/overlay_pipe_{in,out}`. Casing stays Voltage
	// (the tier-colored hull base layer).
	// Part overlay textures live under `block/overlay/machine/` (upstream
	// mirror). Multi-layer composition mirrors upstream model JSONs:
	//   - Item bus / fluid hatch: parent `hatch_machine[_emissive]` ->
	//       casing + `overlay_pipe[_4x|_9x]` + directional + `_emissive`.
	//   - Energy hatch: parent `2_layer/tinted/front` ->
	//       casing + `overlay_energy_Na_tinted` (tintindex 2) + directional +
	//       `_emissive`.
	private const string PartOverlayDir = "block/overlay/machine";

	private static void ItemBus(string id, string label, IO io, VoltageTier[] tiers)
	{
		string ioTok = io == IO.IN ? "in" : "out";
		MachineRegistry.Register(new MachineDefinition
		{
			Id = id, Label = label,
			Family = MachineFamily.ItemBus,
			Tiers = tiers,
			PartIo = io,
			PartAbilities = new[] { io == IO.IN
				? Api.Machine.Multiblock.PartAbility.IMPORT_ITEMS
				: Api.Machine.Multiblock.PartAbility.EXPORT_ITEMS },
			Casing = MachineCasing.Voltage,
			OverlayDir = PartOverlayDir,
			// Layer order (back -> front): casing -> pipe -> directional -> emissive.
			PipeOverlayBasename     = "overlay_pipe",
			OverlayBasename         = io == IO.IN ? "overlay_item_hatch_input" : "overlay_item_hatch_output",
			EmissiveOverlayBasename = $"overlay_pipe_{ioTok}_emissive",
			LayoutKey = "item_bus",
		});
	}

	// One fluid-hatch row. Slots in {1, 4, 9}. Per-tank capacity is derived
	// from slots via `INITIAL_TANK_CAPACITY_{1,4,9}X x 2^min(9, tier)`.
	// 4x / 9x variants swap `overlay_pipe` for `overlay_pipe_{4x,9x}` - the
	// upstream visual cue for slot density. Without it, 1x/4x/9x hatches at
	// the same tier would render pixel-identical.
	private static void FluidHatchDef(string id, string label, IO io, int slots, VoltageTier[] tiers)
	{
		string ioTok = io == IO.IN ? "in" : "out";
		string pipeOverlay = slots switch
		{
			4 => "overlay_pipe_4x",
			9 => "overlay_pipe_9x",
			_ => "overlay_pipe",
		};
		// Abilities: base IMPORT_FLUIDS / EXPORT_FLUIDS + slot-density variant
		// (1X/4X/9X). Mirrors upstream `.abilities(IMPORT_FLUIDS, IMPORT_FLUIDS_NX)`.
		var baseAbility = io == IO.IN ? Api.Machine.Multiblock.PartAbility.IMPORT_FLUIDS
		                              : Api.Machine.Multiblock.PartAbility.EXPORT_FLUIDS;
		var slotAbility = slots switch
		{
			4 => io == IO.IN ? Api.Machine.Multiblock.PartAbility.IMPORT_FLUIDS_4X
			                 : Api.Machine.Multiblock.PartAbility.EXPORT_FLUIDS_4X,
			9 => io == IO.IN ? Api.Machine.Multiblock.PartAbility.IMPORT_FLUIDS_9X
			                 : Api.Machine.Multiblock.PartAbility.EXPORT_FLUIDS_9X,
			_ => io == IO.IN ? Api.Machine.Multiblock.PartAbility.IMPORT_FLUIDS_1X
			                 : Api.Machine.Multiblock.PartAbility.EXPORT_FLUIDS_1X,
		};
		MachineRegistry.Register(new MachineDefinition
		{
			Id = id, Label = label,
			Family = MachineFamily.FluidHatch,
			Tiers = tiers,
			PartIo = io,
			PartFluidSlots = slots,
			PartAbilities = new[] { baseAbility, slotAbility },
			Casing = MachineCasing.Voltage,
			OverlayDir = PartOverlayDir,
			PipeOverlayBasename     = pipeOverlay,
			OverlayBasename         = io == IO.IN ? "overlay_fluid_hatch_input" : "overlay_fluid_hatch_output",
			EmissiveOverlayBasename = $"overlay_pipe_{ioTok}_emissive",
			LayoutKey = "fluid_hatch",
		});
	}

	// One dual-hatch row - item bus + fluid tank on a single face. Slot/tank
	// sizing is tier-derived inside DualHatchPartMachine ((tier-4)^2 slots, sqrt
	// tanks). 3-layer hatch composite, upstream model `part/dual_input_hatch`:
	// casing + `overlay_dual_hatch_{input,output}` + `overlay_pipe_{in,out}_emissive`.
	private static void DualHatch(string id, string label, IO io, VoltageTier[] tiers)
	{
		string ioTok = io == IO.IN ? "in" : "out";
		var abilities = io == IO.IN
			? new[] { Api.Machine.Multiblock.PartAbility.IMPORT_ITEMS,
			          Api.Machine.Multiblock.PartAbility.IMPORT_FLUIDS }
			: new[] { Api.Machine.Multiblock.PartAbility.EXPORT_ITEMS,
			          Api.Machine.Multiblock.PartAbility.EXPORT_FLUIDS };
		MachineRegistry.Register(new MachineDefinition
		{
			Id = id, Label = label,
			Family = MachineFamily.DualHatch,
			Tiers = tiers,
			PartIo = io,
			PartAbilities = abilities,
			Casing = MachineCasing.Voltage,
			OverlayDir = PartOverlayDir,
			PipeOverlayBasename     = "overlay_pipe",
			OverlayBasename         = io == IO.IN ? "overlay_dual_hatch_input" : "overlay_dual_hatch_output",
			EmissiveOverlayBasename = $"overlay_pipe_{ioTok}_emissive",
			LayoutKey = "dual_hatch",
		});
	}

	// One diode row x ELECTRIC_TIERS (LV..UV). Reuses the energy-hatch
	// 1A texture set (overlay_energy_1a_in + tinted + emissive) since the
	// diode boots at 1A; cycling amperage is gameplay-only.
	private static void Diode(string id, string label)
	{
		MachineRegistry.Register(new MachineDefinition
		{
			Id = id, Label = label,
			Family = MachineFamily.Diode,
			Tiers = ElectricTiers,
			PartAbilities = new[] { Api.Machine.Multiblock.PartAbility.PASSTHROUGH_HATCH },
			Casing = MachineCasing.Voltage,
			OverlayDir = PartOverlayDir,
			TintedOverlayBasename   = "overlay_energy_1a_tinted",
			OverlayBasename         = "overlay_energy_1a_in",
			EmissiveOverlayBasename = "overlay_energy_1a_in_emissive",
			LayoutKey = "none",
		});
	}

	// One energy-hatch row. Amperage in {2, 4, 16, 64}. Capacity = `V[tier] x
	// 64 x A` (emitter) / `V[tier] x 16 x A` (receiver) - see
	// `EnergyHatchPartMachine.GetHatchEnergyCapacity`.
	//
	// PNG naming quirk: upstream's 2A hatch uses the `1a` PNG art (single
	// circular cap) - see `energy_input_hatch.json` referencing
	// `overlay_energy_1a_*`. The 4A / 16A / 64A variants use their own art.
	// `pngAmp` maps amperage -> PNG-name prefix accordingly.
	private static void EnergyHatchDef(string id, string label, IO io, int amperage, VoltageTier[] tiers)
	{
		string ioTok = io == IO.IN ? "in" : "out";
		int pngAmp = amperage == 2 ? 1 : amperage;
		// 64A -> SUBSTATION_*, 2/4/16 -> standard INPUT/OUTPUT (upstream split).
		Api.Machine.Multiblock.PartAbility ability;
		if (amperage == 64)
			ability = io == IO.IN ? Api.Machine.Multiblock.PartAbility.SUBSTATION_INPUT_ENERGY
			                      : Api.Machine.Multiblock.PartAbility.SUBSTATION_OUTPUT_ENERGY;
		else
			ability = io == IO.IN ? Api.Machine.Multiblock.PartAbility.INPUT_ENERGY
			                      : Api.Machine.Multiblock.PartAbility.OUTPUT_ENERGY;
		MachineRegistry.Register(new MachineDefinition
		{
			Id = id, Label = label,
			Family = MachineFamily.EnergyHatch,
			Tiers = tiers,
			PartIo = io,
			PartAmperage = amperage,
			PartAbilities = new[] { ability },
			Casing = MachineCasing.Voltage,
			OverlayDir = PartOverlayDir,
			// Layer order (back -> front): casing -> tinted (tier-multiplied) ->
			//   directional -> emissive.
			TintedOverlayBasename   = $"overlay_energy_{pngAmp}a_tinted",
			OverlayBasename         = $"overlay_energy_{pngAmp}a_{ioTok}",
			EmissiveOverlayBasename = $"overlay_energy_{pngAmp}a_{ioTok}_emissive",
			LayoutKey = "none",
		});
	}

	// One laser-hatch row. Verbatim with upstream `registerLaserHatch(io,
	// amperage, ability)` (GTMachineUtils.java:456) - tier loop over
	// LaserHatchTiers (IV..MAX), single-overlay model
	// (`overlayTieredHullModel("laser_target_hatch" | "laser_source_hatch")`).
	//
	// Direction name follows upstream: IN = "target" (receiver), OUT =
	// "source" (emitter). The matching textures live at
	// block/overlay/machine/overlay_laser_{source,target}{,_emissive}.png
	// (already in the upstream-mirror).
	//
	// PartAbility is INPUT_LASER / OUTPUT_LASER - the same tokens
	// ActiveTransformer's hatch predicate lists, so registration alone
	// makes laser hatches valid ActiveTransformer / future PowerSubstation
	// candidates without any predicate edit.
	private static void LaserHatchDef(string id, string label, IO io, int amperage)
	{
		string name = io == IO.IN ? "target" : "source";
		var ability = io == IO.IN
			? Api.Machine.Multiblock.PartAbility.INPUT_LASER
			: Api.Machine.Multiblock.PartAbility.OUTPUT_LASER;
		MachineRegistry.Register(new MachineDefinition
		{
			Id = id, Label = label,
			Family = MachineFamily.LaserHatch,
			Tiers = LaserHatchTiers,
			PartIo = io,
			PartAmperage = amperage,
			PartAbilities = new[] { ability },
			Casing = MachineCasing.Voltage,
			OverlayDir = PartOverlayDir,
			// Single-overlay part per upstream model factory. Emissive added
			// so the active glow shows when EU flows through the hatch.
			OverlayBasename         = $"overlay_laser_{name}",
			EmissiveOverlayBasename = $"overlay_laser_{name}_emissive",
			LayoutKey = "none",
		});
	}

	// Upstream BatteryBufferMachine.java:45-46 amp constants.
	private static class BatteryBufferAmps
	{
		public const long Normal  = 2L;
		public const long Charger = 4L;
	}

	private static void Wtm(string id, string label, Api.Recipe.GTRecipeType recipeType,
		int inSlots, int outSlots, int inTanks = 0, int outTanks = 0,
		bool circuit = false, string layout = "generic")
	{
		MachineRegistry.Register(new MachineDefinition
		{
			Id = id, Label = label,
			Family = MachineFamily.WorkableTiered,
			Tiers = AllTiers,
			RecipeType = recipeType,
			InputSlotCount = inSlots, OutputSlotCount = outSlots,
			InputFluidTankCount = inTanks, OutputFluidTankCount = outTanks,
			UsesCircuit = circuit,
			LayoutKey = layout,
		});
	}

	// One sunlight-powered boiler. Non-tiered - the id matches upstream
	// (lp_steam_solar_boiler). No fuel; the STEAM_BOILER recipe type is
	// nominal - SteamSolarBoiler heats off sunlight, not a fuel recipe.
	private static void SolarBoiler(string id, string label, bool highPressure)
	{
		MachineRegistry.Register(new MachineDefinition
		{
			Id     = id,
			Label  = label,
			Family = MachineFamily.SteamSolarBoiler,
			Tiered = false,
			Tiers  = OneTier,
			RecipeType = GTRecipeTypes.STEAM_BOILER,
			IsHighPressure = highPressure,
			Casing = highPressure ? MachineCasing.BrickedSteel : MachineCasing.BrickedBronze,
			OverlayDir = "block/generators/boiler/solar",
			LayoutKey = "solar_boiler",
		});
	}

	// One liquid-fuel boiler. Non-tiered - id matches upstream
	// (lp_steam_liquid_boiler). Burns the STEAM_BOILER fluid-fuel recipes.
	private static void LiquidBoiler(string id, string label, bool highPressure)
	{
		MachineRegistry.Register(new MachineDefinition
		{
			Id     = id,
			Label  = label,
			Family = MachineFamily.SteamLiquidBoiler,
			Tiered = false,
			Tiers  = OneTier,
			RecipeType = GTRecipeTypes.STEAM_BOILER,
			IsHighPressure = highPressure,
			Casing = highPressure ? MachineCasing.BrickedSteel : MachineCasing.BrickedBronze,
			OverlayDir = "block/generators/boiler/lava",
			LayoutKey = "liquid_boiler",
		});
	}

	// One steam processing machine - registers both an LP and an HP variant.
	private static void Steam(string shortName, string label, Api.Recipe.GTRecipeType recipeType,
		int inSlots, int outSlots)
	{
		SteamVariant(shortName, label, recipeType, inSlots, outSlots, "lp", "Steam ",    highPressure: false);
		SteamVariant(shortName, label, recipeType, inSlots, outSlots, "hp", "HP Steam ", highPressure: true);
	}

	// Non-tiered: the id IS the MachineKey ("lp_steam_macerator"), matching
	// upstream. One SimpleSteamMachine family entity backs every variant.
	private static void SteamVariant(string shortName, string label, Api.Recipe.GTRecipeType recipeType,
		int inSlots, int outSlots, string prefix, string pressureLabel, bool highPressure)
	{
		MachineRegistry.Register(new MachineDefinition
		{
			Id     = $"{prefix}_steam_{shortName}",
			Label  = pressureLabel + label,
			Family = MachineFamily.SimpleSteam,
			Tiered = false,
			Tiers  = OneTier,
			RecipeType = recipeType,
			InputSlotCount = inSlots, OutputSlotCount = outSlots,
			IsHighPressure = highPressure,
			Casing = highPressure ? MachineCasing.BrickedSteel : MachineCasing.BrickedBronze,
			// Upstream's workableSteamHullModel reuses the electric machine's
			// overlay (block/machines/macerator), NOT a steam-specific one -
			// so point at the shared overlay rather than deriving from the id.
			OverlayDir = $"block/machines/{shortName}",
			LayoutKey = "steam_machine",
		});
	}

	// One steam-powered mining drill - registers lp_steam_miner / hp_steam_miner
	// separately so the per-variant texture path can differ (upstream ships
	// `block/machines/steam_miner` for LP and `.../high_pressure_steam_miner`
	// for HP). Non-tiered: the id IS the MachineKey, matching upstream.
	private static void SteamMiner(string id, string label, string overlayDir, bool highPressure)
	{
		MachineRegistry.Register(new MachineDefinition
		{
			Id     = id, Label = label,
			Family = MachineFamily.SteamMiner,
			Tiered = false, Tiers = OneTier,
			// No recipe type - mining is world-driven, like the electric
			// MinerMachine. Upstream's `recipeType(DUMMY_RECIPES)` is a
			// stub to satisfy SteamWorkableMachine; we don't extend that.
			IsHighPressure = highPressure,
			Casing = highPressure ? MachineCasing.BrickedSteel : MachineCasing.BrickedBronze,
			OverlayDir = overlayDir,
			// Upstream multipart model paints `overlay_front` on the front face
			// (the steam pressure dial on the bronze brick casing). Matches the
			// electric Miner + every other workableSteamHullModel machine.
			// The drill animation lives on `overlay_bottom` - it points DOWN at
			// the ore in upstream's 3D model, and in our 2D world the machine's
			// bottom face is into-the-ground (invisible to the player), so the
			// drill strip isn't a useful front-face pick. See "Static-front
			// machines with active-on-different-face" deferred-polish row.
			OverlayBasename = "overlay_front",
			LayoutKey = "steam_miner",
		});
	}

	private static void Battery(string id, string label, int slots, long inputAmpsPerItem, long outputAmps)
	{
		MachineRegistry.Register(new MachineDefinition
		{
			Id = id, Label = label,
			Family = MachineFamily.BatteryBuffer,
			Tiers = AllTiers,
			BatterySlotCount = slots,
			InputAmpsPerItem = inputAmpsPerItem,
			OutputAmps = outputAmps,
			OverlayDir = "block/machines/charger",
			OverlayBasename = "overlay_charger_idle",
			LayoutKey = "battery_buffer",
		});
	}

	// PatternFactory closure for `coke_oven`. Builds a `BlockPattern` from
	// the hand-authored 2D shape in `MultiblockShapes.CokeOven` + a char ->
	// predicate map. Tile lookups are deferred (closure) so they fire AFTER
	// `TieredMachineFactory` has registered both tiles.
	//
	// Pattern:
	//   "XXX"   X = casing OR hatch
	//   "XYX"   Y = controller (this multi)
	//   "XXX"
	//
	// Upstream `setMaxGlobalLimited(5)` on the hatch slot is dropped per
	// MultiblockShapes.cs convention (numbers don't translate at 2D scale).
	// PatternFactory closure for `electric_blast_furnace`. Uses the
	// `MultiblockShapes.ElectricBlastFurnace` fixed 5x5 shape.
	//
	// Char legend:
	//   X - heatproof casing OR any allowed hatch (energy/item/fluid/maintenance)
	//   C - heating coil (any single tier - captured into MatchContext)
	//   S - controller
	//   M - muffler hatch
	// PatternFactory for `large_chemical_reactor`. Verbatim port of upstream's
	// `.pattern(...)` predicate chain - every `.where(ch, ...)` upstream
	// declares is mirrored here, even if the user-authored shape in
	// `MultiblockShapes.LargeChemicalReactor` doesn't currently use the char.
	// Predicates that aren't referenced by the shape sit unused but stay
	// registered so the shape can grow without touching this factory.
	//
	// Upstream `.where(...)` chain:
	//   X = casing.setMinGlobalLimited(10) | autoAbilities(definition.recipeTypes)
	//                                      | autoAbilities(true, false, false)
	//   C = heatingCoils().setExactLimit(1) | abilities | casing
	//   P = blocks(CASING_POLYTETRAFLUOROETHYLENE_PIPE.get())
	//   S = controller(blocks(definition.block))
	private static IBlockPattern BuildLCRPattern()
	{
		// Mirror of GTMultiMachines.java:203-218 .pattern() chain. The 'C' cell
		// upstream is `heatingCoils().setExactLimit(1).or(abilities).or(casing)`
		// - we keep the same composition; coil OR (any wall-side hatch) OR
		// (PTFE inert casing) - which matches "this cell is either THE coil or
		// just another wall cell" verbatim.
		var lcrDef    = MachineRegistry.Get("large_chemical_reactor")!;
		var casing    = Predicates.Blocks("inert_machine_casing");
		var abilities = Predicates.AutoAbilities(GTRecipeTypes.LARGE_CHEMICAL_RECIPES)
			.Or(Predicates.AutoAbilities(true, false, false));

		return new BlockPattern(MultiblockShapes.LargeChemicalReactor, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(lcrDef),
			['X'] = casing.Or(abilities),
			['P'] = Predicates.Blocks("ptfe_pipe_casing"),
			['C'] = Predicates.HeatingCoils().SetExactLimit(1).Or(abilities).Or(casing),
		});
	}

	// === Standard processing-multi factories ===================================
	// Each is a line-by-line mirror of upstream's `.pattern(definition -> ...)`
	// block. The `StandardWall(...)` helper expands to upstream's
	// `blocks(casing).or(autoAbilities(recipeTypes)).or(autoAbilities(maint, muffler, parallel))`
	// composition - see `Predicates.StandardWall` for the per-channel min/max
	// derivation (lazy-checked against loaded recipes).
	private static IBlockPattern BuildVacuumFreezerPattern()
	{
		// Mirror of GTMultiMachines.java:517-526.
		var def = MachineRegistry.Get("vacuum_freezer")!;
		return new BlockPattern(MultiblockShapes.VacuumFreezer, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("frostproof_machine_casing", new[] { GTRecipeTypes.VACUUM_RECIPES }),
		});
	}

	private static IBlockPattern BuildImplosionCompressorPattern()
	{
		// Mirror of GTMultiMachines.java:269-277. autoAbilities(true, true, false)
		// = maint + muffler (no separate 'M' cell - muffler accepted in walls).
		var def = MachineRegistry.Get("implosion_compressor")!;
		return new BlockPattern(MultiblockShapes.ImplosionCompressor, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("solid_machine_casing",
				new[] { GTRecipeTypes.IMPLOSION_RECIPES }, maintenance: true, muffler: true),
		});
	}

	private static IBlockPattern BuildLargeAutoclavePattern()
	{
		// Mirror of GCYMMachines.java:517-529. 'T' cell = CASING_STEEL_PIPE.
		var def = MachineRegistry.Get("large_autoclave")!;
		return new BlockPattern(MultiblockShapes.LargeAutoclave, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("watertight_casing",
				new[] { GTRecipeTypes.AUTOCLAVE }, maintenance: true, parallel: true),
			['T'] = Predicates.Blocks("steel_pipe_casing"),
		});
	}

	// Mirror of GTMultiMachines.java:447-470 (distillation_tower). 2D-collapsed
	// to a vertical column - `MultiblockShapes.DistillationTower`.
	//   Y = controller row casing + recipe-typed I/O abilities (input fluid /
	//       input energy / output items) + maintenance.
	//   Z = wall casing OR per-layer EXPORT_FLUIDS OR maintenance - verbatim
	//       upstream Z = casing.or(exportPredicate).or(maint). In a minimal
	//       3-tall tower the two Y cells fill with INPUT_ENERGY + IMPORT_
	//       FLUIDS, so the required maintenance has to land on a Z cell.
	//   X = wall casing OR one EXPORT_FLUIDS per layer (body + head rows are
	//       all output layers).
	//
	// Upstream computes `maint` once as a local and passes the SAME
	// `TraceabilityPredicate` to both Y and Z. The matcher's GlobalCount is
	// keyed by `SimplePredicate` identity - sharing the instance means a
	// hatch in Y or Z increments ONE counter. We can't use `StandardWall`
	// for Y here (it would build its own maint instance), so the predicate
	// is composed manually mirroring upstream's chain order.
	private static IBlockPattern BuildDistillationTowerPattern()
	{
		var def = MachineRegistry.Get("distillation_tower")!;
		var casing = Predicates.Blocks("clean_machine_casing");
		// Verbatim upstream `GTMultiMachines.java:447` - per-layer EXPORT must
		// be a 1x-capacity hatch (`PartAbility.EXPORT_FLUIDS_1X`), not the
		// generic `EXPORT_FLUIDS`. Without the slot-density filter, the player
		// could install 4x / 9x hatches on a layer and silently dilute output.
		var perLayerExport = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.EXPORT_FLUIDS_1X).SetMaxLayerLimited(1);
		var maint = Predicates.AutoAbilities(checkMaintenance: true, checkMuffler: false, checkParallel: false);
		// Y wall verbatim from upstream (GTMultiMachines.java:461-466). Three
		// ability cells have explicit limits - AutoAbilities defaults to
		// `SetPreviewCount(1)` only, which silently drops these caps, so we
		// compose the wall manually here:
		//   - INPUT_ENERGY: min 1, max 2 (matches AutoAbilities default for IN_ENERGY).
		//   - EXPORT_ITEMS: max 1 (upstream `setMaxGlobalLimited(1)`).
		//   - IMPORT_FLUIDS: exact 1 (upstream `setExactLimit(1)` - the recipe-
		//     EU fluid input; matcher rejects the structure without it).
		var energyIn   = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.INPUT_ENERGY)
			.SetMinGlobalLimited(1).SetMaxGlobalLimited(2);
		var itemOut    = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.EXPORT_ITEMS)
			.SetMaxGlobalLimited(1);
		var fluidIn    = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.IMPORT_FLUIDS)
			.SetExactLimit(1);
		var yWall = casing.Or(energyIn).Or(itemOut).Or(fluidIn).Or(maint);
		return new RepeatableBlockPattern(MultiblockShapes.DistillationTower, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['Y'] = yWall,
			['Z'] = casing.Or(perLayerExport).Or(maint),
			['X'] = casing.Or(perLayerExport),
			['A'] = Predicates.Air(),
		});
	}

	// Mirror of GCYMMachines.java:619-655 (large_distillery). 2D-collapsed to a
	// vertical column with a steel-pipe core - `MultiblockShapes.LargeDistillery`.
	//   Y = controller row + tail/front walls - casing + IMPORT_ITEMS / INPUT_
	//       ENERGY(2) / IMPORT_FLUIDS / EXPORT_ITEMS / maintenance / parallel
	//       hatch (no EXPORT_FLUIDS - upstream Y predicate doesn't include it).
	//   X = wall casing OR one EXPORT_FLUIDS per layer.
	//   Z = wall casing only (top cap).
	//   P = steel_pipe_casing (central core).
	//   A = air.
	//   # = any (decorative corners).
	private static IBlockPattern BuildLargeDistilleryPattern()
	{
		var def = MachineRegistry.Get("large_distillery")!;
		var casing = Predicates.Blocks("watertight_casing");
		// Verbatim upstream `GCYMMachines.java:631` - see distillation_tower
		// comment above; same EXPORT_FLUIDS_1X requirement.
		var perLayerExport = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.EXPORT_FLUIDS_1X).SetMaxLayerLimited(1);
		// Y wall verbatim from upstream (GCYMMachines.java:640-644). Compose
		// manually because the ability cells have explicit limits the generic
		// AutoAbilities helper would drop to `SetPreviewCount(1)`:
		//   - INPUT_ENERGY: min 1, max 2.
		//   - EXPORT_ITEMS: max 1.
		//   - IMPORT_FLUIDS: min 1 (note: upstream uses min, NOT exact - the
		//     player can stack multiple input hatches for higher throughput;
		//     compare to distillation_tower's `setExactLimit(1)`).
		var energyIn = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.INPUT_ENERGY)
			.SetMinGlobalLimited(1).SetMaxGlobalLimited(2);
		var itemOut  = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.EXPORT_ITEMS)
			.SetMaxGlobalLimited(1);
		var fluidIn  = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.IMPORT_FLUIDS)
			.SetMinGlobalLimited(1);
		var maintParallel = Predicates.AutoAbilities(checkMaintenance: true, checkMuffler: false, checkParallel: true);
		var yWall = casing.Or(energyIn).Or(itemOut).Or(fluidIn).Or(maintParallel);
		return new RepeatableBlockPattern(MultiblockShapes.LargeDistillery, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['Y'] = yWall,
			['Z'] = casing,
			['X'] = casing.Or(perLayerExport),
			['P'] = Predicates.Blocks("steel_pipe_casing"),
			['A'] = Predicates.Air(),
			['#'] = Predicates.Any(),
		});
	}

	// `Std(...)` - registers one of the bulk-ported standard processing multis.
	// Same family (shared electric entity), same layout, same modifier; only
	// id / label / casing / overlay / pattern differ.
	private static void Std(string id, string label, Func<IBlockPattern> patternFactory,
		string fusedCasingTile, string overlayDir, params Api.Recipe.GTRecipeType[] recipeTypes)
	{
		// Multi-mode multis (extractor+canner / cutter+lathe / large_brewer's
		// 3 recipe types) get the array; single-mode multis populate both
		// RecipeType and RecipeTypes so any reader path resolves. Without this
		// `WorkableMultiblockMachine.GetRecipeType` throws - the ServerTick
		// runs RecipeLogic.SearchRecipe every frame, which calls GetRecipeType.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = id, Label = label,
			Family = MachineFamily.MultiblockElectricStandard,
			Tiered = false, Tiers = OneTier,
			Casing = MachineCasing.Voltage,
			OverlayDir = overlayDir,
			OverlayBasename = "overlay_front",
			LayoutKey = "generic_multi",
			PatternFactory = patternFactory,
			MultiRecipeModifier = GTRecipeModifiers.OC_NON_PERFECT_SUBTICK,
			FusedCasingTileName = fusedCasingTile,
			RecipeType  = recipeTypes.Length > 0 ? recipeTypes[0] : null,
			RecipeTypes = recipeTypes.Length > 0 ? recipeTypes : null,
		});
	}

	// `Coil(...)` - registers a coil-based multi (the 5 Tier-2 multis). Uses
	// the shared `CoilWorkableElectricMultiblockMachine` entity (coil tier
	// captured on form via `Predicates.HeatingCoils`); modifier supplied per
	// row (multiSmelterParallel / pyrolyseOvenOverclock / crackerOverclock /
	// ebfOverclock - each is coil-tier-aware).
	private static void Coil(string id, string label, Func<IBlockPattern> patternFactory,
		string fusedCasingTile, string overlayDir, Api.Recipe.Modifier.RecipeModifier modifier,
		Action<MetaMachine, List<string>>? additionalDisplay,
		params Api.Recipe.GTRecipeType[] recipeTypes)
	{
		// Single recipe-type goes into `RecipeType`; multi-mode (multi_smelter)
		// goes into `RecipeTypes[]` so `WorkableMultiblockMachine.GetRecipeType()`
		// picks up both. Set both at construction since the properties are init-only.
		MachineRegistry.Register(new MachineDefinition
		{
			Id = id, Label = label,
			Family = MachineFamily.MultiblockCoilStandard,
			Tiered = false, Tiers = OneTier,
			Casing = MachineCasing.Voltage,
			OverlayDir = overlayDir,
			OverlayBasename = "overlay_front",
			LayoutKey = "generic_multi",
			PatternFactory = patternFactory,
			MultiRecipeModifier = modifier,
			AdditionalDisplay   = additionalDisplay,
			FusedCasingTileName = fusedCasingTile,
			RecipeType  = recipeTypes.Length == 1 ? recipeTypes[0] : null,
			RecipeTypes = recipeTypes.Length > 1  ? recipeTypes    : null,
		});
	}

	// `LargeCombustionEngine(...)` - registers one combustion-engine controller
	// (large_combustion_engine EV / extreme_combustion_engine IV). Tier rides
	// in `Tiers[0]` so `MetaMachine.Tier` returns it; `IsGenerator = true`
	// flips the multi's EU routing to push through OUTPUT_ENERGY hatches.
	private static void LargeCombustionEngine(string id, string label, int tier,
		string casingTile, string gearTile, string intakeTile, string overlayDir)
	{
		MachineRegistry.Register(new MachineDefinition
		{
			Id = id, Label = label,
			Family = MachineFamily.MultiblockLargeCombustionEngine,
			Tiered = false, Tiers = new[] { (VoltageTier)tier },
			RecipeType = GTRecipeTypes.COMBUSTION,
			IsGenerator = true,
			Casing = MachineCasing.Voltage,
			OverlayDir = overlayDir, OverlayBasename = "overlay_front",
			LayoutKey = "generic_multi",
			PatternFactory = () => BuildLargeCombustionEnginePattern(id, tier, casingTile, gearTile, intakeTile),
			// No `MultiRecipeModifier` - the entity overrides `GetRecipeModifier`
			// directly with `CombustionModifier`, which has the lubricant gate
			// and oxygen boost the static def-level modifier can't express.
			FusedCasingTileName = casingTile,
		});
	}

	// Shared pattern factory for both combustion engines. Mirror of upstream
	// `GTMachineUtils.registerLargeCombustionEngine.pattern(...)`. 2D-collapsed
	// shape lives in `MultiblockShapes.LargeCombustionEngine`. Per-tier
	// (casing / gear / intake) tile names bound at construction.
	//
	// Char legend (upstream):
	//   Y - controller
	//   X - appearance casing (stable / robust)
	//   D - OUTPUT_ENERGY hatch at tier >= controller tier (verbatim upstream
	//       `ability(OUTPUT_ENERGY, IntStream.of(ULV..UHV).filter(t >= tier))`
	//       - prevents the player from placing a low-tier dynamo and detonating
	//       it on first tick via the over-voltage path in NotifiableEnergyContainer)
	//   C - appearance casing OR autoAbilities (item in/out, fluid in/out - fuel
	//       + lubricant + ash recovery + oxygen hatches) OR maintenance
	//   G - gearbox (NOT in our 2D shape - collapsed away, predicate kept so
	//       the shape can grow without touching the factory)
	//   A - intake (engine_intake / extreme_engine_intake)
	private static IBlockPattern BuildLargeCombustionEnginePattern(string id, int tier,
		string casingTile, string gearTile, string intakeTile)
	{
		var def = MachineRegistry.Get(id)!;
		var casing = Predicates.Blocks(casingTile);
		// Verbatim upstream tier filter: ULV..UHV, drop anything < controller tier.
		var allowedTiers = new[]
		{
			(int)VoltageTier.ULV, (int)VoltageTier.LV, (int)VoltageTier.MV, (int)VoltageTier.HV,
			(int)VoltageTier.EV,  (int)VoltageTier.IV, (int)VoltageTier.LuV,
			(int)VoltageTier.ZPM, (int)VoltageTier.UV, (int)VoltageTier.UHV,
		}.Where(t => t >= tier).ToArray();
		var energyOut = Predicates.Ability(Api.Machine.Multiblock.PartAbility.OUTPUT_ENERGY, allowedTiers);
		// `C` cell - upstream:
		//   blocks(casing).setMinGlobalLimited(3)
		//     .or(autoAbilities(recipeTypes, false, false, true, true, true, true))
		//     .or(autoAbilities(true, true, false))   // maint + muffler
		var cCell = casing
			.Or(Predicates.AutoAbilities(new[] { GTRecipeTypes.COMBUSTION },
				checkEnergyIn: false, checkEnergyOut: false,
				checkItemIn: true, checkItemOut: true,
				checkFluidIn: true, checkFluidOut: true))
			.Or(Predicates.AutoAbilities(checkMaintenance: true, checkMuffler: true, checkParallel: false));
		return new BlockPattern(MultiblockShapes.LargeCombustionEngine,
			new Dictionary<char, TraceabilityPredicate>
		{
			['Y'] = Predicates.Controller(def),
			['X'] = casing,
			['D'] = energyOut,
			['C'] = cCell,
			['G'] = Predicates.Blocks(gearTile),
			['A'] = Predicates.Blocks(intakeTile),
		});
	}

	// `LargeTurbine(...)` - registers one turbine controller (steam HV / gas
	// EV / plasma IV). `RecipeType` is passed in per-fuel-kind.
	private static void LargeTurbine(string id, string label, VoltageTier tier,
		Api.Recipe.GTRecipeType recipeType, string casingTile, string gearTile,
		string overlayDir, bool needsMuffler)
	{
		MachineRegistry.Register(new MachineDefinition
		{
			Id = id, Label = label,
			Family = MachineFamily.MultiblockLargeTurbine,
			Tiered = false, Tiers = new[] { tier },
			RecipeType = recipeType,
			IsGenerator = true,
			Casing = MachineCasing.Voltage,
			OverlayDir = overlayDir, OverlayBasename = "overlay_front",
			LayoutKey = "generic_multi",
			PatternFactory = () => BuildLargeTurbinePattern(id, casingTile, gearTile, needsMuffler),
			// Modifier supplied by LargeTurbineMachine.GetRecipeModifier override
			// (rotor-power-scaled - needs runtime rotor stats; can't be a
			// static def-level modifier).
			FusedCasingTileName = casingTile,
		});
	}

	// Shared pattern factory for all three turbines. Mirror of upstream
	// `GTMachineUtils.registerLargeTurbine.pattern(...)`. 2D-collapsed shape
	// in `MultiblockShapes.LargeTurbine`. Per-fuel-kind (casing / gear /
	// needsMuffler) bound at construction.
	//
	// Char legend (upstream):
	//   S - controller
	//   C - appearance casing
	//   G - gearbox (NOT in our 2D shape - collapsed away)
	//   R - ROTOR_HOLDER (exact 1) OR OUTPUT_ENERGY (exact 1) - two R cells in
	//       the shape; one is the rotor mount, the other is the dynamo hatch
	//   H - appearance casing OR autoAbilities (fuel input fluids / output items
	//       for distilled water etc.) OR maintenance + optional muffler
	private static IBlockPattern BuildLargeTurbinePattern(string id,
		string casingTile, string gearTile, bool needsMuffler)
	{
		var def = MachineRegistry.Get(id)!;
		var casing = Predicates.Blocks(casingTile);
		// Read the turbine's recipe type back off the def (set in LargeTurbine
		// above) - autoAbilities gates each hatch slot on whether the recipe
		// type actually uses that capability (gas/plasma have no fluid output;
		// steam has distilled water output).
		var recipeType = def.RecipeType!;
		var hCell = casing
			.Or(Predicates.AutoAbilities(new[] { recipeType },
				checkEnergyIn: false, checkEnergyOut: false,
				checkItemIn: true, checkItemOut: true,
				checkFluidIn: true, checkFluidOut: true))
			.Or(Predicates.AutoAbilities(checkMaintenance: true, checkMuffler: needsMuffler, checkParallel: false));
		// `R` cell - upstream is the rotor-holder predicate `.setExactLimit(1)
		// .or(abilities(OUTPUT_ENERGY)).setExactLimit(1)`. The `.setExactLimit`
		// on the OR'd branch only applies to that branch - so each of (rotor /
		// dynamo) has exact 1.
		var rotorHolder = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.ROTOR_HOLDER)
			.SetExactLimit(1);
		var energyOut = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.OUTPUT_ENERGY)
			.SetExactLimit(1);
		return new BlockPattern(MultiblockShapes.LargeTurbine,
			new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['C'] = casing,
			['G'] = Predicates.Blocks(gearTile),
			['R'] = rotorHolder.Or(energyOut),
			['H'] = hCell,
		});
	}

	// `LargeMiner(...)` - registers one large_miner controller per tier
	// (EV / IV / LuV). Casing + frame-material vary per tier exactly as
	// upstream `LargeMinerMachine.getCasingState` / `getMaterial`. RecipeType
	// is DUMMY (no recipe loop - the controller drives itself off OnTick;
	// see `LargeMinerMachine`).
	private static void LargeMiner(string id, string label, VoltageTier tier,
		string casingTile, string frameMaterialId)
	{
		MachineRegistry.Register(new MachineDefinition
		{
			Id = id, Label = label,
			Family = MachineFamily.MultiblockLargeMiner,
			Tiered = false, Tiers = new[] { tier },
			// Browser-display station only - `LargeMinerMachine.IsRecipeLogicAvailable`
			// returns false so RecipeLogic stays dormant. Synth recipes populated
			// by `BiomeWorldIORecipeSynth` at Mod.Load.
			RecipeType = GTRecipeTypes.LARGE_MINER,
			Casing = MachineCasing.Voltage,
			OverlayDir = "block/multiblock/large_miner", OverlayBasename = "overlay_front",
			LayoutKey = "generic_multi",
			PatternFactory = () => BuildLargeMinerPattern(id, casingTile, frameMaterialId),
			FusedCasingTileName = casingTile,
		});
	}

	// Shared pattern factory for all three large_miner tiers. Mirror of
	// upstream `GTMultiMachines.LARGE_MINER` `.pattern(...)`. The 2D-collapsed
	// shape lives in `MultiblockShapes.LargeMiner`. Char legend (upstream):
	//   S - controller
	//   X - appearance casing OR EXPORT_ITEMS (1) OR IMPORT_FLUIDS (1) OR
	//       INPUT_ENERGY (1-2)
	//   C - appearance casing
	//   F - frames(material) at this tier - `Predicates.Frames`
	//   # - any
	private static IBlockPattern BuildLargeMinerPattern(string id,
		string casingTile, string frameMaterialId)
	{
		var def    = MachineRegistry.Get(id)!;
		var casing = Predicates.Blocks(casingTile);
		// X cell - verbatim with upstream's `.where('X', blocks(...) .or(EXPORT_ITEMS)
		// .or(IMPORT_FLUIDS) .or(INPUT_ENERGY))`. setExactLimit(1) on EXPORT_ITEMS
		// + IMPORT_FLUIDS, INPUT_ENERGY min 1 max 2.
		var exportItems  = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.EXPORT_ITEMS).SetExactLimit(1);
		var importFluids = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.IMPORT_FLUIDS).SetExactLimit(1);
		var energyIn     = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.INPUT_ENERGY)
			.SetMinGlobalLimited(1).SetMaxGlobalLimited(2);
		var frameMat = Common.Materials.MaterialRegistry.Get(frameMaterialId)
			?? throw new System.InvalidOperationException(
				$"LargeMiner({id}): material '{frameMaterialId}' not found in MaterialRegistry");
		return new BlockPattern(MultiblockShapes.LargeMiner, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = casing.Or(exportItems).Or(importFluids).Or(energyIn),
			['C'] = casing,
			['F'] = Predicates.Frames(frameMat),
		});
	}

	// `FluidDrillingRig(...)` - registers one fluid_drilling_rig controller per
	// tier (MV / HV / EV). Casing + frame mirror upstream `FluidDrillMachine.
	// getCasingState` / `getFrameState`. RecipeType = DUMMY (controller is
	// self-driven off OnTick; see `FluidDrillingRigMachine`).
	private static void FluidDrillingRig(string id, string label, VoltageTier tier,
		string casingTile, string frameMaterialId)
	{
		MachineRegistry.Register(new MachineDefinition
		{
			Id = id, Label = label,
			Family = MachineFamily.MultiblockFluidDrillingRig,
			Tiered = false, Tiers = new[] { tier },
			// Browser-display station only - see `LargeMinerMachine` comment above.
			RecipeType = GTRecipeTypes.FLUID_DRILLING_RIG,
			Casing = MachineCasing.Voltage,
			OverlayDir = "block/multiblock/fluid_drilling_rig", OverlayBasename = "overlay_front",
			LayoutKey = "generic_multi",
			PatternFactory = () => BuildFluidDrillingRigPattern(id, casingTile, frameMaterialId),
			FusedCasingTileName = casingTile,
		});
	}

	// Shared pattern factory for all three fluid_drilling_rig tiers. Mirror
	// of upstream `GTMultiMachines.FLUID_DRILLING_RIG` `.pattern(...)`. The
	// 2D-collapsed shape lives in `MultiblockShapes.FluidDrillingRig`. Char
	// legend (upstream):
	//   S - controller
	//   X - appearance casing OR INPUT_ENERGY (max 2) OR EXPORT_FLUIDS (max 1)
	//   C - appearance casing
	//   F - frames(material) at this tier
	//   # - any
	private static IBlockPattern BuildFluidDrillingRigPattern(string id,
		string casingTile, string frameMaterialId)
	{
		var def    = MachineRegistry.Get(id)!;
		var casing = Predicates.Blocks(casingTile);
		var energyIn   = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.INPUT_ENERGY)
			.SetMinGlobalLimited(1).SetMaxGlobalLimited(2);
		var fluidOut   = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.EXPORT_FLUIDS)
			.SetMaxGlobalLimited(1);
		var frameMat = Common.Materials.MaterialRegistry.Get(frameMaterialId)
			?? throw new System.InvalidOperationException(
				$"FluidDrillingRig({id}): material '{frameMaterialId}' not found in MaterialRegistry");
		return new BlockPattern(MultiblockShapes.FluidDrillingRig, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = casing.Or(energyIn).Or(fluidOut),
			['C'] = casing,
			['F'] = Predicates.Frames(frameMat),
		});
	}

	// `FusionReactor(...)` - registers one fusion_reactor controller per tier
	// (LuV / ZPM / UV). Casing + coil tile vary per tier exactly as upstream
	// `FusionReactorMachine.getCasingState` / `getCoilState`. RecipeType =
	// FUSION_RECIPES; the bespoke FUSION_OC modifier handles startup-cost
	// payment from the capacitor and overclock.
	private static void FusionReactor(string id, string label, VoltageTier tier,
		string casingTile, string coilTile)
	{
		MachineRegistry.Register(new MachineDefinition
		{
			Id = id, Label = label,
			Family = MachineFamily.MultiblockFusionReactor,
			Tiered = false, Tiers = new[] { tier },
			// `gtceu:fusion_reactor` recipes carry `eu_to_start`. The FUSION_OC
			// modifier reads that field, pays the cost from the capacitor, and
			// applies the perfect-half-duration overclock.
			RecipeType = GTRecipeTypes.FUSION_REACTOR,
			Casing = MachineCasing.Voltage,
			OverlayDir = "block/multiblock/fusion_reactor", OverlayBasename = "overlay_front",
			LayoutKey = "generic_multi",
			PatternFactory = () => BuildFusionReactorPattern(id, casingTile, coilTile),
			MultiRecipeModifier = GTRecipeModifiers.FUSION_OC,
			FusedCasingTileName = casingTile,
		});
	}

	// Shared pattern factory for all three fusion tiers. Mirror of upstream
	// `GTMultiMachines.FUSION_REACTOR` (lines 385-415). The 2D-collapsed shape
	// lives in `MultiblockShapes.FusionReactor`. Char legend (upstream):
	//   S - controller
	//   C - casing (tier-keyed via `casingTile`)
	//   K - coil (tier-keyed via `coilTile` - superconducting/fusion)
	//   G - fusion_glass OR casing (the windowed ring)
	//   E - casing OR INPUT_ENERGY hatch in [tier..UV] (upstream
	//       `getBlockRange(tier, UV)` filter; `SetPreviewCount(16)`)
	//   O - casing OR EXPORT_FLUIDS hatch
	//   I - casing OR IMPORT_FLUIDS hatch
	//   A - air (interior)
	//   # - any
	private static IBlockPattern BuildFusionReactorPattern(string id,
		string casingTile, string coilTile)
	{
		var def    = MachineRegistry.Get(id)!;
		var casing = Predicates.Blocks(casingTile);
		var glass  = Predicates.Blocks("fusion_glass").Or(casing);
		var coil   = Predicates.Blocks(coilTile);
		// Verbatim with upstream `blocks(PartAbility.INPUT_ENERGY.getBlockRange
		// (tier, UV).toArray(Block[]::new))` - the hatch must be in [reactorTier,
		// UV]. A LuV reactor accepts LuV/ZPM/UV hatches only; ZPM accepts
		// ZPM/UV; UV accepts UV. Lower-tier hatches are rejected at the
		// structure-matching stage, NOT silently downvolted at run time -
		// matches upstream player expectations.
		int reactorTier = def.Tiers[0] is VoltageTier vt ? (int)vt : (int)VoltageTier.LuV;
		var hatchTiles = Api.Machine.Multiblock.PartAbility.INPUT_ENERGY
			.GetTileRange(reactorTier, (int)VoltageTier.UV);
		// Verbatim with upstream `setMinGlobalLimited(1).setPreviewCount(16)`.
		var energyIn = Predicates.Blocks(System.Linq.Enumerable.ToArray(hatchTiles))
			.SetMinGlobalLimited(1).SetPreviewCount(16);
		// Verbatim with upstream `.setMinGlobalLimited(2)` on the I cell.
		var fluidIn  = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.IMPORT_FLUIDS).SetMinGlobalLimited(2);
		var fluidOut = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.EXPORT_FLUIDS);
		return new BlockPattern(MultiblockShapes.FusionReactor, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['C'] = casing,
			['K'] = coil,
			['G'] = glass,
			['E'] = casing.Or(energyIn),
			['O'] = casing.Or(fluidOut),
			['I'] = casing.Or(fluidIn),
			// Upstream's 15x15 ring has an air-filled cavity. 'A' is the
			// interior; the matcher requires it to be empty (no tile present).
			// '#' = any cell is auto-mapped by BlockPattern.
			['A'] = Predicates.Air(),
		});
	}

	// Mirror of GTMultiMachines.ACTIVE_TRANSFORMER pattern (GTMultiMachines.java:967-975).
	// 2D collapse of upstream's 3x3x3 cube lives in `MultiblockShapes.ActiveTransformer`
	// (XXX / XCX / XSX). Char legend:
	//   S - controller
	//   C - superconducting_coil (the central inductor; verbatim upstream)
	//   X - high_power_casing OR any of the six hatch abilities upstream lists
	//
	// Hatch predicate borrows POWER_SUBSTATION's predicate shape (upstream
	// GTMultiMachines.java:1009-1012) - input + output abilities grouped by
	// IO direction, each group with `.setMinGlobalLimited(1)` so the
	// matcher rejects hatchless structures with an actionable candidate
	// list ("Need 1 of {input_energy, substation_input_energy, input_laser}")
	// instead of silently invalidating in `OnStructureFormed`.
	//
	// (INPUT_LASER/OUTPUT_LASER currently have no registered part defs;
	// they remain in the candidate set so the structure auto-recognises
	// laser hatches when those land - no later edit needed.)
	//
	// === Documented adaptations =================================================
	//
	//   - DEVIATION: upstream's `getHatchPredicates`
	//     (ActiveTransformerMachine.java:162-169) uses per-ability
	//     `setPreviewCount` only - no min. Upstream RELIES on the controller's
	//     `onStructureFormed` to silently bail when `powerInput.isEmpty() ||
	//     powerOutput.isEmpty()`, producing the same generic "Invalid
	//     Structure!" message a 2D player has no good way to act on.
	//     We borrow upstream's OWN sibling POWER_SUBSTATION predicate shape
	//     (verbatim from GTMultiMachines.java:1009-1012) so the matcher
	//     refuses early with a player-actionable candidate list. Follows
	//     codebase convention (steam_grinder.SetExactLimit, distillation_tower
	//     .SetExactLimit, etc.) - if the predicate can encode the requirement,
	//     it should.
	//   - Upstream's `.setMinGlobalLimited(12)` on the casing predicate is a
	//     3D-cube constant - the cube has 27 X-cells (25 after subtracting
	//     controller + coil). Our 2D shape has 7 X-cells total; min 12 is
	//     unsatisfiable. Scaled to `SetMinGlobalLimited(2)` - keeps the
	//     spirit ("most cells are casing, hatches are the minority") within
	//     the available cell budget. Casing min isn't a balance lever
	//     upstream - it's an anti-cheese guard.
	private static IBlockPattern BuildActiveTransformerPattern()
	{
		var def    = MachineRegistry.Get("active_transformer")!;
		var casing = Predicates.Blocks("high_power_casing").SetMinGlobalLimited(2);
		var coil   = Predicates.Blocks("superconducting_coil");
		// Verbatim with POWER_SUBSTATION's predicate (upstream
		// GTMultiMachines.java:1009-1012) - one predicate per IO direction,
		// matching ANY of the three input (or output) variants. The
		// `setMinGlobalLimited(1)` forces the matcher to find at least one
		// satisfying part of each group before forming the structure.
		var inputHatch = Predicates.Abilities(
			Api.Machine.Multiblock.PartAbility.INPUT_ENERGY,
			Api.Machine.Multiblock.PartAbility.SUBSTATION_INPUT_ENERGY,
			Api.Machine.Multiblock.PartAbility.INPUT_LASER).SetMinGlobalLimited(1);
		var outputHatch = Predicates.Abilities(
			Api.Machine.Multiblock.PartAbility.OUTPUT_ENERGY,
			Api.Machine.Multiblock.PartAbility.SUBSTATION_OUTPUT_ENERGY,
			Api.Machine.Multiblock.PartAbility.OUTPUT_LASER).SetMinGlobalLimited(1);
		return new BlockPattern(MultiblockShapes.ActiveTransformer, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['C'] = coil,
			['X'] = casing.Or(inputHatch).Or(outputHatch),
		});
	}

	// Mirror of GTMultiMachines.POWER_SUBSTATION pattern (GTMultiMachines.java:997-1015).
	// 2D collapse of upstream's 5x4xN cube (N = battery layers) lives in
	// MultiblockShapes.PowerSubstation - user-authored single-layer shape:
	//   "GGGGG"   <- laminated glass top
	//   "GBBBG"   <- glass walls, 3 battery cells inside
	//   "XCCCX"   <- substation casing wall, 3 inner casing
	//   "XXSXX"   <- substation casing wall + controller centered + walls
	//
	// Char legend (upstream):
	//   S - controller (palladium_substation casing)
	//   C - palladium_substation casing
	//   G - laminated_glass casing
	//   B - powerSubstationBatteries predicate (any of 9 battery blocks)
	//   X - palladium_substation casing OR maintenance hatch OR input/output
	//       energy variants OR substation/laser variants
	//
	// === Documented adaptations =================================================
	//
	//   - Upstream `setMinGlobalLimited(MIN_CASINGS=14)` is calibrated to its
	//     27-X-cell 3D shape (5x5xN - 5 controller-row-removals). Our 2D
	//     collapse has 6 X cells total (rows 2-3 minus the controller). Scaled
	//     to `SetMinGlobalLimited(3)` - keeps the "most X cells are casing"
	//     intent within our cell budget.
	//   - Upstream `autoAbilities(true, false, false)` on X cells adds the
	//     maintenance hatch ability. Maintenance isn't a ported subsystem;
	//     dropped here (the controller's GetPassiveDrain returns the raw rate
	//     without maintenance multipliers).
	//   - Battery predicate composes from PssBatteryData.All (9 tile names);
	//     no `setMinGlobalLimited(1)` because the controller's OnStructureFormed
	//     invariant (`if (batteries.isEmpty()) onStructureInvalid()`) already
	//     rejects an empty-battery structure with an actionable line via the
	//     persisted-unformed-reason path.
	private static IBlockPattern BuildPowerSubstationPattern()
	{
		var def    = MachineRegistry.Get("power_substation")!;
		var casing = Predicates.Blocks("palladium_substation").SetMinGlobalLimited(3);
		var glass  = Predicates.Blocks("laminated_glass");

		// Battery predicate - matches ANY of the 9 PSS battery tile names.
		// Empty fillers (empty_tier_i/ii/iii) are accepted in the structure
		// (upstream-verbatim "can use as visual filler"); only filled batteries
		// contribute to the energy bank's capacity via PssBatteryData.Tier > -1.
		var batteryTileNames = new List<string>(
			Multiblock.Electric.PssBatteryData.All.Keys);
		var batteries = Predicates.Blocks(batteryTileNames.ToArray());

		// IO hatch groups - verbatim with upstream lines 1009-1012 (same shape
		// the active_transformer port also uses). `SetMinGlobalLimited(1)` on
		// each group forces the matcher to find at least one input + one output
		// before forming the structure, with a player-actionable error otherwise.
		var inputHatch = Predicates.Abilities(
			Api.Machine.Multiblock.PartAbility.INPUT_ENERGY,
			Api.Machine.Multiblock.PartAbility.SUBSTATION_INPUT_ENERGY,
			Api.Machine.Multiblock.PartAbility.INPUT_LASER).SetMinGlobalLimited(1);
		var outputHatch = Predicates.Abilities(
			Api.Machine.Multiblock.PartAbility.OUTPUT_ENERGY,
			Api.Machine.Multiblock.PartAbility.SUBSTATION_OUTPUT_ENERGY,
			Api.Machine.Multiblock.PartAbility.OUTPUT_LASER).SetMinGlobalLimited(1);

		return new BlockPattern(MultiblockShapes.PowerSubstation, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['C'] = casing,
			['G'] = glass,
			['B'] = batteries,
			['X'] = casing.Or(inputHatch).Or(outputHatch),
		});
	}

	// Mirror of GTMultiMachines.STEAM_GRINDER pattern (GTMultiMachines.java:600-620).
	// 2D collapse: 3x3 of bronze brick casing with the controller in the center;
	// the upstream `#` interior-air cell has no 2D representation, so the user-
	// authored shape drops it (see `MultiblockShapes.SteamGrinder` - `XXX/XSX/XXX`).
	// Char legend (upstream `.where(...)`):
	//   S - controller
	//   X - bronze_brick_casing OR steam_input_bus OR steam_output_bus OR
	//       steam_input_hatch (the recipe-EU source - `SetExactLimit(1)` ensures
	//       the matcher surfaces a "need 1 steam_input_hatch" error if the
	//       player builds an all-casing shell; upstream `setExactLimit(1)`
	//       verbatim).
	private static IBlockPattern BuildSteamGrinderPattern()
	{
		var def    = MachineRegistry.Get("steam_grinder")!;
		var casing = Predicates.Blocks("bronze_brick_casing");
		var steamIn  = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.STEAM_IMPORT_ITEMS);
		var steamOut = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.STEAM_EXPORT_ITEMS);
		var steamEnergy = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.STEAM).SetExactLimit(1);
		return new BlockPattern(MultiblockShapes.SteamGrinder, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = casing.Or(steamIn).Or(steamOut).Or(steamEnergy),
		});
	}

	// Mirror of GTMultiMachines.STEAM_OVEN pattern (GTMultiMachines.java:622-648).
	// 2D collapse: 3x3 with top-corners as walk-through (`#`), top-middle and
	// the rest of the perimeter as bronze brick casing or steam I/O bus, plus
	// the firebox `F` row at the bottom. Upstream's 3D structure has a 3-wide
	// firebox sole on the BACK aisle (`FFF`); we collapse to a single firebox
	// `F` cell on the bottom-middle face (the user-authored shape - see
	// `MultiblockShapes.SteamOven` - keeps just the front face). Char legend:
	//   S - controller
	//   X - bronze_brick_casing OR steam_input_bus OR steam_output_bus
	//   F - bronze_firebox_casing OR steam_input_hatch (the steam-energy source)
	private static IBlockPattern BuildSteamOvenPattern()
	{
		var def         = MachineRegistry.Get("steam_oven")!;
		var casing      = Predicates.Blocks("bronze_brick_casing");
		var firebox     = Predicates.Blocks("bronze_firebox_casing");
		var steamIn     = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.STEAM_IMPORT_ITEMS);
		var steamOut    = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.STEAM_EXPORT_ITEMS);
		// SetExactLimit(1) - matcher surfaces "need 1 steam_input_hatch" if missing.
		var steamEnergy = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.STEAM).SetExactLimit(1);
		return new BlockPattern(MultiblockShapes.SteamOven, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = casing.Or(steamIn).Or(steamOut),
			['F'] = firebox.Or(steamEnergy),
			['#'] = Predicates.Any(),
		});
	}

	// Mirror of GTMultiMachines.ASSEMBLY_LINE pattern (GTMultiMachines.java:538-561).
	// 2D collapse follows the user-authored horizontal shape in
	// `MultiblockShapes.AssemblyLine` (RepeatAxis.Horizontal - Head/Body/Tail
	// = left/middle/right column-segments; Body column repeats 3..15 times).
	// Char legend:
	//   S - controller (Head row 1, col 0 - top-left of the line)
	//   Y - steel casing OR INPUT_ENERGY (Head row 0, col 0 - top-left)
	//   F - steel casing OR IMPORT_FLUIDS_1X (Head rows 2..3, col 0 - left column)
	//   I - LV input bus (Head row 3 col 1, plus each Body[3] - runs left-to-right)
	//   O - output bus EXPORT_ITEMS ability (Tail row 3 - right end of the line)
	//   G - assembly_line_grating (Head row 0 col 1, plus each Body[0], plus Tail[0])
	//   D - grate OR DATA_ACCESS_HATCH (Head row 1 col 1, plus each Body[1], plus Tail[1])
	//   R - laminated_glass (Head row 2 col 1, plus each Body[2], plus Tail[2])
	// Bus ordering: I cells are walked left-to-right by
	// `AssemblyLineMachine.GetPartSorter` (X-ascending); the N-th I is the
	// N-th input bus.
	private static IBlockPattern BuildAssemblyLinePattern()
	{
		var def = MachineRegistry.Get("assembly_line")!;
		var steel = Predicates.Blocks("steel_machine_casing");
		// `Y` cell - steel OR INPUT_ENERGY. Upstream `setMinGlobalLimited(1).setMaxGlobalLimited(2)`.
		var energyIn   = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.INPUT_ENERGY);
		// `F` cell - steel OR IMPORT_FLUIDS_1X. Upstream's predicate gates on
		// `ConfigHolder.orderedAssemblyLineFluids`: when true (default), only
		// the LV 1x fluid hatch is allowed (capped at 4 globally); when false,
		// 1x/4x/9x are all allowed. We mirror the default-true behavior - just
		// IMPORT_FLUIDS_1X. The `setMaxGlobalLimited(4)` cap is dropped (matches
		// our convention for 2D-collapsed shapes - see assembler / other GCYM).
		var fluidImport = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.IMPORT_FLUIDS_1X);
		// `D` cell - grate OR DATA_ACCESS_HATCH. Upstream
		// `dataHatchPredicate(blocks(CASING_GRATE.get()))`. Now that the research
		// subsystem is ported, this mirrors upstream's research-enabled branch:
		// EXACTLY ONE of (a local Data Access Hatch) OR (an Optical Data Reception
		// Hatch - feeds research from a remote Data Bank over optical pipes),
		// else plain grating. The setExactLimit(1) caps it at one data hatch
		// across the whole (repeatable) structure.
		var dataAccess = Predicates.Abilities(
				Api.Machine.Multiblock.PartAbility.DATA_ACCESS,
				Api.Machine.Multiblock.PartAbility.OPTICAL_DATA_RECEPTION)
			.SetExactLimit(1);
		return new RepeatableBlockPattern(MultiblockShapes.AssemblyLine,
			new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['I'] = Predicates.Blocks("ulv_input_bus"),  // upstream ITEM_IMPORT_BUS[0] = ULV (index 0 of ALL_TIERS)
			['O'] = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.EXPORT_ITEMS),
			['G'] = Predicates.Blocks("assembly_line_grating"),
			['D'] = Predicates.Blocks("assembly_line_grating").Or(dataAccess),
			['R'] = Predicates.Blocks("laminated_glass"),
			['Y'] = steel.Or(energyIn),
			['F'] = steel.Or(fluidImport),
		});
	}

	// === Research / computation pattern factories =========================
	// Line-for-line mirrors of GTResearchMachines.java `.where(...)` chains,
	// mapped onto the user-authored shapes in MultiblockShapes. Where the 2D
	// shape collapsed several upstream cell roles into one char, that char's
	// predicate is the union of the collapsed roles (documented per cell).

	private static IBlockPattern BuildHpcaPattern()
	{
		var def = MachineRegistry.Get("high_performance_computation_array")!;
		return new BlockPattern(MultiblockShapes.HighPerformanceComputationArray,
			new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['A'] = Predicates.Blocks("advanced_computer_casing"),
			['V'] = Predicates.Blocks("computer_heat_vent"),
			['X'] = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.HPCA_COMPONENT),
			// COMPUTATION_DATA_TRANSMISSION setExactLimit(1) retained verbatim
			// (codebase convention: drop setMinGlobalLimited, keep setExactLimit)
			// so the matcher rejects an HPCA with no transmission hatch with an
			// actionable "need 1 ..." count error instead of forming idle.
			['C'] = Predicates.Blocks("computer_casing")
				.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.INPUT_ENERGY))
				.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.IMPORT_FLUIDS))
				.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.COMPUTATION_DATA_TRANSMISSION).SetExactLimit(1))
				.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.MAINTENANCE)),
		});
	}

	// Data Bank: the authored shape collapsed upstream's A/C/D roles into D, so
	// D is the catch-all (casing | data hatches | input energy | maintenance).
	private static IBlockPattern BuildDataBankPattern()
	{
		var def = MachineRegistry.Get("data_bank")!;
		return new BlockPattern(MultiblockShapes.DataBank,
			new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.Blocks("computer_heat_vent"),
			['D'] = Predicates.Blocks("computer_casing")
				.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.DATA_ACCESS))
				.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.OPTICAL_DATA_TRANSMISSION))
				.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.OPTICAL_DATA_RECEPTION))
				.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.INPUT_ENERGY))
				.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.MAINTENANCE)),
		});
	}

	private static IBlockPattern BuildNetworkSwitchPattern()
	{
		var def = MachineRegistry.Get("network_switch")!;
		return new BlockPattern(MultiblockShapes.NetworkSwitch,
			new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.Blocks("computer_casing")
				.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.INPUT_ENERGY))
				.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.COMPUTATION_DATA_TRANSMISSION))
				.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.COMPUTATION_DATA_RECEPTION))
				.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.MAINTENANCE)),
		});
	}

	// Research Station: P=casing|input energy|computation reception|maintenance;
	// X=casing; V=heat vent; A=advanced casing; H=OBJECT_HOLDER; #=any.
	private static IBlockPattern BuildResearchStationPattern()
	{
		var def = MachineRegistry.Get("research_station")!;
		return new BlockPattern(MultiblockShapes.ResearchStation,
			new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.Blocks("computer_casing"),
			['V'] = Predicates.Blocks("computer_heat_vent"),
			['A'] = Predicates.Blocks("advanced_computer_casing"),
			['H'] = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.OBJECT_HOLDER),
			// COMPUTATION_DATA_RECEPTION setExactLimit(1) retained verbatim - the
			// research station can't run without a computation receiver, so the
			// matcher must require exactly one (actionable "need 1 ..." error
			// instead of forming then bailing). H already requires the object
			// holder via the OBJECT_HOLDER ability.
			['P'] = Predicates.Blocks("computer_casing")
				.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.INPUT_ENERGY))
				.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.COMPUTATION_DATA_RECEPTION).SetExactLimit(1))
				.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.MAINTENANCE)),
		});
	}

	// ============================================================================
	// *** Standard processing-multi factories ***
	// ============================================================================
	// Each is a line-by-line mirror of its upstream `.pattern(definition -> ...)`
	// chain in GTMultiMachines.java / GCYMMachines.java. EVERY upstream
	// `.where(...)` predicate is ported even when the current 2D-collapsed
	// shape doesn't yet use the char - upstream is the source of truth.
	//
	// 2D adaptations:
	//   - `setMinGlobalLimited(N)` dropped - calibrated for 3D shapes; see
	//     `Predicates.StandardWall` for the rationale.
	//   - `setExactLimit(1)` retained verbatim - kept so a single-allowed cell
	//     stays single.

	private static IBlockPattern BuildLargeCentrifugePattern()
	{
		var def = MachineRegistry.Get("large_centrifuge")!;
		return new BlockPattern(MultiblockShapes.LargeCentrifuge, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("vibration_safe_casing",
				new[] { GTRecipeTypes.CENTRIFUGE, GTRecipeTypes.THERMAL_CENTRIFUGE }, parallel: true),
			['P'] = Predicates.Blocks("steel_pipe_casing"),
			['A'] = Predicates.Air(),
		});
	}

	private static IBlockPattern BuildLargeElectrolyzerPattern()
	{
		var def = MachineRegistry.Get("large_electrolyzer")!;
		return new BlockPattern(MultiblockShapes.LargeElectrolyzer, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("nonconducting_casing",
				new[] { GTRecipeTypes.ELECTROLYZER }, parallel: true),
			['C'] = Predicates.Blocks("electrolytic_cell"),
		});
	}

	private static IBlockPattern BuildLargeElectromagnetPattern()
	{
		var def = MachineRegistry.Get("large_electromagnet")!;
		return new BlockPattern(MultiblockShapes.LargeElectromagnet, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("nonconducting_casing",
				new[] { GTRecipeTypes.ELECTROMAGNETIC_SEPARATOR, GTRecipeTypes.POLARIZER }, parallel: true),
			['C'] = Predicates.Blocks("electrolytic_cell"),
		});
	}

	private static IBlockPattern BuildLargePackerPattern()
	{
		var def = MachineRegistry.Get("large_packer")!;
		return new BlockPattern(MultiblockShapes.LargePacker, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("robust_machine_casing",
				new[] { GTRecipeTypes.PACKER }, parallel: true),
			['A'] = Predicates.Air(),
		});
	}

	private static IBlockPattern BuildLargeAssemblerPattern()
	{
		// GCYMMachines.java:296-308. Custom wall predicate: NO INPUT_ENERGY in
		// autoAbilities - INPUT_ENERGY is its own setExactLimit(1) cell.
		var def = MachineRegistry.Get("large_assembler")!;
		var wall = Predicates.Blocks("large_scale_assembler_casing")
			.Or(Predicates.AutoAbilities(new[] { GTRecipeTypes.ASSEMBLER },
				checkEnergyIn: false, checkEnergyOut: false,
				checkItemIn: true, checkItemOut: true, checkFluidIn: true, checkFluidOut: true))
			.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.INPUT_ENERGY).SetExactLimit(1))
			.Or(Predicates.AutoAbilities(checkMaintenance: true, checkMuffler: false, checkParallel: true));
		return new BlockPattern(MultiblockShapes.LargeAssembler, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = wall,
			['G'] = Predicates.Blocks("tempered_glass"),
			['A'] = Predicates.Air(),
		});
	}

	private static IBlockPattern BuildLargeCircuitAssemblerPattern()
	{
		// GCYMMachines.java:328-345. Same exact-1 INPUT_ENERGY treatment.
		var def = MachineRegistry.Get("large_circuit_assembler")!;
		var wall = Predicates.Blocks("large_scale_assembler_casing")
			.Or(Predicates.AutoAbilities(new[] { GTRecipeTypes.CIRCUIT_ASSEMBLER },
				checkEnergyIn: false, checkEnergyOut: false,
				checkItemIn: true, checkItemOut: true, checkFluidIn: true, checkFluidOut: true))
			.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.INPUT_ENERGY).SetExactLimit(1))
			.Or(Predicates.AutoAbilities(checkMaintenance: true, checkMuffler: false, checkParallel: true));
		return new BlockPattern(MultiblockShapes.LargeCircuitAssembler, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = wall,
			['T'] = Predicates.Blocks("tempered_glass"),
			['G'] = Predicates.Blocks("assembly_line_grating"),
			['P'] = Predicates.Blocks("tungstensteel_pipe_casing"),
			['A'] = Predicates.Air(),
		});
	}

	private static IBlockPattern BuildLargeArcSmelterPattern()
	{
		// GCYMMachines.java:360-374. 'C' = MOLYBDENUM_DISILICIDE_COIL_BLOCK
		// (specific tile, NOT heatingCoils()). 'M' = MUFFLER cell.
		var def = MachineRegistry.Get("large_arc_smelter")!;
		return new BlockPattern(MultiblockShapes.LargeArcSmelter, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("high_temperature_smelting_casing",
				new[] { GTRecipeTypes.ARC_FURNACE }, parallel: true),
			['C'] = Predicates.Blocks("molybdenum_disilicide_coil_block"),
			['M'] = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.MUFFLER),
			['A'] = Predicates.Air(),
		});
	}

	private static IBlockPattern BuildLargeEngravingLaserPattern()
	{
		// GCYMMachines.java:392-406. 'C' = TUNGSTENSTEEL_PIPE.
		var def = MachineRegistry.Get("large_engraving_laser")!;
		return new BlockPattern(MultiblockShapes.LargeEngravingLaser, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("laser_safe_engraving_casing",
				new[] { GTRecipeTypes.LASER_ENGRAVER }, parallel: true),
			['C'] = Predicates.Blocks("tungstensteel_pipe_casing"),
			['G'] = Predicates.Blocks("tempered_glass"),
			['K'] = Predicates.Blocks("assembly_line_grating"),
			['A'] = Predicates.Air(),
		});
	}

	private static IBlockPattern BuildLargeSiftingFunnelPattern()
	{
		// GCYMMachines.java:421-433. 'K' = GRATE.
		var def = MachineRegistry.Get("large_sifting_funnel")!;
		return new BlockPattern(MultiblockShapes.LargeSiftingFunnel, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("vibration_safe_casing",
				new[] { GTRecipeTypes.SIFTER }, parallel: true),
			['K'] = Predicates.Blocks("assembly_line_grating"),
			['A'] = Predicates.Air(),
		});
	}

	private static IBlockPattern BuildLargeMaterialPressPattern()
	{
		// GCYMMachines.java:545-556. Recipe types: bender + compressor +
		// forge_hammer + forming_press. 'G' = STEEL_GEARBOX, 'C' = TEMPERED_GLASS.
		var def = MachineRegistry.Get("large_material_press")!;
		return new BlockPattern(MultiblockShapes.LargeMaterialPress, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("stress_proof_casing",
				new[] { GTRecipeTypes.BENDER, GTRecipeTypes.COMPRESSOR,
					GTRecipeTypes.FORGE_HAMMER, GTRecipeTypes.FORMING_PRESS }, parallel: true),
			['G'] = Predicates.Blocks("steel_gearbox"),
			['C'] = Predicates.Blocks("tempered_glass"),
			['A'] = Predicates.Air(),
		});
	}

	private static IBlockPattern BuildLargeBrewerPattern()
	{
		// GCYMMachines.java:572-586. 'P' = STEEL_PIPE, 'C' =
		// MOLYBDENUM_DISILICIDE_COIL_BLOCK (specific tile).
		var def = MachineRegistry.Get("large_brewer")!;
		return new BlockPattern(MultiblockShapes.LargeBrewer, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("corrosion_proof_casing",
				new[] { GTRecipeTypes.BREWERY, GTRecipeTypes.FERMENTER, GTRecipeTypes.FLUID_HEATER },
				parallel: true),
			['P'] = Predicates.Blocks("steel_pipe_casing"),
			['C'] = Predicates.Blocks("molybdenum_disilicide_coil_block"),
			['A'] = Predicates.Air(),
		});
	}

	private static IBlockPattern BuildLargeCutterPattern()
	{
		// GCYMMachines.java:601-613. 'G' = TEMPERED_GLASS, 'C' = SLICING_BLADES.
		var def = MachineRegistry.Get("large_cutter")!;
		return new BlockPattern(MultiblockShapes.LargeCutter, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("shock_proof_cutting_casing",
				new[] { GTRecipeTypes.CUTTER, GTRecipeTypes.LATHE }, parallel: true),
			['G'] = Predicates.Blocks("tempered_glass"),
			['C'] = Predicates.Blocks("slicing_blades"),
			['A'] = Predicates.Air(),
		});
	}

	private static IBlockPattern BuildLargeExtractorPattern()
	{
		// GCYMMachines.java:718-728. 'C' = STEEL_PIPE.
		var def = MachineRegistry.Get("large_extractor")!;
		return new BlockPattern(MultiblockShapes.LargeExtractor, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("watertight_casing",
				new[] { GTRecipeTypes.EXTRACTOR, GTRecipeTypes.CANNER }, parallel: true),
			['C'] = Predicates.Blocks("steel_pipe_casing"),
			['A'] = Predicates.Air(),
		});
	}

	private static IBlockPattern BuildLargeExtruderPattern()
	{
		// GCYMMachines.java:743-757. 'P' = TITANIUM_PIPE, 'G' = TEMPERED_GLASS.
		var def = MachineRegistry.Get("large_extruder")!;
		return new BlockPattern(MultiblockShapes.LargeExtruder, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("stress_proof_casing",
				new[] { GTRecipeTypes.EXTRUDER }, parallel: true),
			['P'] = Predicates.Blocks("titanium_pipe_casing"),
			['G'] = Predicates.Blocks("tempered_glass"),
			['A'] = Predicates.Air(),
		});
	}

	private static IBlockPattern BuildLargeSolidifierPattern()
	{
		// GCYMMachines.java:772-786. 'C' = STEEL_PIPE.
		var def = MachineRegistry.Get("large_solidifier")!;
		return new BlockPattern(MultiblockShapes.LargeSolidifier, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("watertight_casing",
				new[] { GTRecipeTypes.FLUID_SOLIDIFIER }, parallel: true),
			['C'] = Predicates.Blocks("steel_pipe_casing"),
			['A'] = Predicates.Air(),
		});
	}

	private static IBlockPattern BuildLargeWiremillPattern()
	{
		// GCYMMachines.java:800-810. 'C' = TITANIUM_GEARBOX.
		var def = MachineRegistry.Get("large_wiremill")!;
		return new BlockPattern(MultiblockShapes.LargeWiremill, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("stress_proof_casing",
				new[] { GTRecipeTypes.WIREMILL }, parallel: true),
			['C'] = Predicates.Blocks("titanium_gearbox"),
		});
	}

	private static IBlockPattern BuildMegaVacuumFreezerPattern()
	{
		// GCYMMachines.java:921-938. Multi-aux: vents + pipes + glass + stainless-clean.
		var def = MachineRegistry.Get("mega_vacuum_freezer")!;
		return new BlockPattern(MultiblockShapes.MegaVacuumFreezer, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("frostproof_machine_casing",
				new[] { GTRecipeTypes.VACUUM_RECIPES }, parallel: true),
			['G'] = Predicates.Blocks("tempered_glass"),
			['K'] = Predicates.Blocks("clean_machine_casing"),
			['P'] = Predicates.Blocks("tungstensteel_pipe_casing"),
			['V'] = Predicates.Blocks("heat_vent"),
			['A'] = Predicates.Air(),
		});
	}

	// ============================================================================
	// *** Coil-based multi factories (Tier-2) ***
	// ============================================================================
	// Mirror upstream's `.pattern(definition -> ...)` chains in GTMultiMachines.java
	// and GCYMMachines.java. Each multi's wall predicate is the same StandardWall
	// composition; the heating-coil cell uses `Predicates.HeatingCoils()` (which
	// captures the coil tier into the match context for the row's recipe modifier
	// to consume).

	private static IBlockPattern BuildMultiSmelterPattern()
	{
		// Mirror of GTMultiMachines.java:344-352. autoAbilities(true, false, false)
		// = maint only. Separate 'M' cell = MUFFLER ability.
		var def = MachineRegistry.Get("multi_smelter")!;
		return new BlockPattern(MultiblockShapes.MultiSmelter, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("heatproof_machine_casing",
				new[] { GTRecipeTypes.ELECTRIC_FURNACE, GTRecipeTypes.ALLOY_SMELTER }),
			['C'] = Predicates.HeatingCoils(),
			['M'] = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.MUFFLER),
		});
	}

	private static IBlockPattern BuildPyrolyseOvenPattern()
	{
		// Mirror of GTMultiMachines.java:289-301. autoAbilities(true, true, false)
		// = maint + muffler in walls (no separate 'M' cell).
		var def = MachineRegistry.Get("pyrolyse_oven")!;
		return new BlockPattern(MultiblockShapes.PyrolyseOven, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("ulv_machine_casing",
				new[] { GTRecipeTypes.PYROLYSE_RECIPES }, muffler: true),
			['C'] = Predicates.HeatingCoils(),
		});
	}

	private static IBlockPattern BuildCrackerPattern()
	{
		// Mirror of GTMultiMachines.java:397-407. autoAbilities(true, true, false).
		// 'O' = controller (not 'S'), 'H' = walls (not 'X'). Verbatim from upstream.
		var def = MachineRegistry.Get("cracker")!;
		return new BlockPattern(MultiblockShapes.Cracker, new Dictionary<char, TraceabilityPredicate>
		{
			['O'] = Predicates.Controller(def),
			['H'] = Predicates.StandardWall("clean_machine_casing",
				new[] { GTRecipeTypes.CRACKING_RECIPES }, muffler: true),
			['C'] = Predicates.HeatingCoils(),
		});
	}

	private static IBlockPattern BuildAlloyBlastSmelterPattern()
	{
		// Mirror of GCYMMachines.java:451-466. autoAbilities(true, false, false)
		// + separate 'M' cell (MUFFLER), 'C' coil, 'G' HEAT_VENT, 'F' frame
		// (HastelloyX material - falls back to plain blocks lookup since our
		// frame tile is `<material>_frame`).
		var def = MachineRegistry.Get("alloy_blast_smelter")!;
		return new BlockPattern(MultiblockShapes.AlloyBlastSmelter, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("high_temperature_smelting_casing",
				new[] { GTRecipeTypes.ALLOY_BLAST_RECIPES }),
			['C'] = Predicates.HeatingCoils(),
			['M'] = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.MUFFLER),
			['G'] = Predicates.Blocks("heat_vent"),
			['F'] = Predicates.Blocks("hastelloy_x_frame"),
		});
	}

	private static IBlockPattern BuildMegaBlastFurnacePattern()
	{
		// Mirror of GCYMMachines.java:829-859. Massive 13x13x17 upstream shape
		// collapsed to user-authored 2D shape. autoAbilities(true, false, true)
		// = maint + parallel-hatch (no muffler in walls - separate 'M' cell).
		var def = MachineRegistry.Get("mega_blast_furnace")!;
		return new BlockPattern(MultiblockShapes.MegaBlastFurnace, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("high_temperature_smelting_casing",
				new[] { GTRecipeTypes.BLAST_RECIPES }, parallel: true),
			['C'] = Predicates.HeatingCoils(),
			['M'] = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.MUFFLER),
			['T'] = Predicates.Blocks("robust_machine_casing"),
			['B'] = Predicates.Blocks("tungstensteel_firebox_casing"),
			['P'] = Predicates.Blocks("tungstensteel_pipe_casing"),
			['I'] = Predicates.Blocks("extreme_engine_intake_casing"),
			['V'] = Predicates.Blocks("heat_vent"),
			['F'] = Predicates.Blocks("naquadah_alloy_frame"),
			['A'] = Predicates.Air(),
		});
	}

	private static IBlockPattern BuildEBFPattern()
	{
		// Mirror of GTMultiMachines.java:140-151 .pattern() chain.
		var ebfDef = MachineRegistry.Get("electric_blast_furnace")!;
		return new BlockPattern(MultiblockShapes.ElectricBlastFurnace, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(ebfDef),
			['X'] = Predicates.StandardWall("heatproof_machine_casing", new[] { GTRecipeTypes.BLAST_RECIPES }),
			['C'] = Predicates.HeatingCoils(),
			['M'] = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.MUFFLER),
		});
	}

	// PatternFactory closure for `cleanroom`. Resolves the four wall/glass/filter
	// tile types lazily (after CasingRegistry has run) and builds a
	// `RepeatableBlockPattern` from `MultiblockShapes.Cleanroom`.
	//
	// Char legend:
	//   X - sealed wall (plascrete / cleanroom_glass / energy hatch /
	//       maintenance hatch / passthrough hatch / iron door)
	//   F - cleanroom filter casing (filter_casing / sterilizing_filter_casing)
	//   S - controller (this multi)
	//   ' ' - interior cell: `CleanroomMachine.InnerPredicateMatch` (accepts
	//       all non-banned MetaMachines + air; accumulates receivers).
	private static IBlockPattern BuildCleanroomPattern()
	{
		// Mirror of GTMultiMachines.java:864-882 .pattern() chain. Cleanroom
		// doesn't use autoAbilities upstream - its walls accept PASSTHROUGH_HATCH
		// (max 30) + INPUT_ENERGY (min 1, max 3) + MAINTENANCE (exact 1) alongside
		// the casing. Verbatim per-ability declaration.
		var cleanroomDef = MachineRegistry.Get("cleanroom")!;
		var wallPred = Predicates.Blocks("plascrete", "cleanroom_glass")
			// Upstream `setMaxGlobalLimited(3, 2)` - 3 globally, 2 per face. We
			// only enforce the global; per-face has no 2D analogue.
			.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.INPUT_ENERGY)
				.SetMinGlobalLimited(1).SetMaxGlobalLimited(3))
			// Upstream `blocks(MAINTENANCE_HATCH).setExactLimit(1)`. We bind to
			// the ability (same set in our port) + ExactLimit(1) for parity.
			.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.MAINTENANCE)
				.SetExactLimit(1))
			// Upstream `setMaxGlobalLimited(30, 3)` - 30 globally, 3 per face.
			.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.PASSTHROUGH_HATCH)
				.SetMaxGlobalLimited(30));

		return new RepeatableBlockPattern(MultiblockShapes.Cleanroom, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(cleanroomDef),
			['X'] = wallPred,
			['F'] = Predicates.CleanroomFilters(),
			[' '] = Predicates.Custom(Multiblock.Electric.CleanroomMachine.InnerPredicateMatch,
				() => System.Array.Empty<Terraria.Item>()),
		});
	}

	private static IBlockPattern BuildCokeOvenPattern()
	{
		var cokeOvenDef = MachineRegistry.Get("coke_oven")!;
		// Verbatim upstream `GTMultiMachines.java:105` - the coke_oven_hatch is
		// `setMaxGlobalLimited(5)`. Without the cap, the player could replace
		// every casing cell with a hatch and end up with a structurally-valid
		// but unworkable build (no brick walls). Split the X cell into
		// `coke_oven_bricks` (uncapped) + `coke_oven_hatch` (max 5).
		return new BlockPattern(MultiblockShapes.CokeOven, new Dictionary<char, TraceabilityPredicate>
		{
			['Y'] = Predicates.Controller(cokeOvenDef),
			['X'] = Predicates.Blocks("coke_oven_bricks")
				.Or(Predicates.Blocks("coke_oven_hatch").SetMaxGlobalLimited(5)),
		});
	}

	// Register one Large Boiler tier row. Per-tier (casing / pipe / firebox)
	// tile names + (maxTemp / heatSpeed) numbers; everything else identical
	// across tiers - shared pattern factory built lazily from the row's tile
	// names. Recipe-type is `LARGE_BOILER` (fuel items + duration).
	private static void LargeBoiler(string tierId, string label,
		string casingTile, string pipeTile, string fireboxTile,
		int maxTemperature, int heatSpeed)
	{
		string id = $"{tierId}_large_boiler";
		MachineRegistry.Register(new MachineDefinition
		{
			Id = id, Label = label,
			Family = MachineFamily.MultiblockLargeBoiler,
			Tiered = false, Tiers = OneTier,
			RecipeType = GTRecipeTypes.LARGE_BOILER,
			BoilerMaxTemperature = maxTemperature,
			BoilerHeatSpeed      = heatSpeed,
			// Controller renders with the tier's appearance casing - mirrors
			// upstream `appearanceBlock(casing)`. Bound parts reskin to it too
			// (FusedCasingTileName drives both paths).
			FusedCasingTileName = casingTile,
			OverlayDir          = $"block/multiblock/generator/large_{tierId}_boiler",
			OverlayBasename     = "overlay_front",
			LayoutKey           = "large_boiler",
			PatternFactory      = () => BuildLargeBoilerPattern(id, casingTile, pipeTile, fireboxTile),
		});
	}

	// Shared pattern factory for all 4 boiler tiers - same shape, per-tier
	// (casing / pipe / firebox) bound at construction. Mirror of upstream
	// registerLargeBoiler's .pattern(...) block (GTMachineUtils.java:725-735).
	//
	// Shape (2D-collapsed front face):
	//   CCC   wall casing
	//   CPC   wall casing + pipe + wall casing
	//   CSC   wall casing + CONTROLLER + wall casing
	//   XXX   firebox row (also accepts INPUT_FLUIDS, IMPORT_ITEMS, MUFFLER -
	//         we accept just casing/firebox for v1; the C cells already accept
	//         EXPORT_FLUIDS for steam output, and upstream's X-cell hatch
	//         fallbacks are deferred until we exercise the secondary slots)
	private static IBlockPattern BuildLargeBoilerPattern(string id,
		string casingTile, string pipeTile, string fireboxTile)
	{
		var def = MachineRegistry.Get(id)!;
		// Verbatim upstream `GTMachineUtils.java:714-733`. Each cell has its
		// own per-ability cap - without these, a Large Boiler can form with
		// 0 fireboxes / 0 muffler / 0 fluid hatches and silently fail to
		// produce steam, OR conversely form with too many of one ability.
		//   C cell - casing (no min upstream) OR EXPORT_FLUIDS min 1 (steam out)
		//   X cell - firebox min 3 OR IMPORT_FLUIDS min 1 (water in) OR
		//            IMPORT_ITEMS max 1 (solid fuel) OR MUFFLER exact 1
		var casingOrExportFluids = Predicates.Blocks(casingTile)
			.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.EXPORT_FLUIDS)
				.SetMinGlobalLimited(1));
		var fireboxOrInputs = Predicates.Blocks(fireboxTile).SetMinGlobalLimited(3)
			.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.IMPORT_FLUIDS)
				.SetMinGlobalLimited(1))
			.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.IMPORT_ITEMS)
				.SetMaxGlobalLimited(1))
			.Or(Predicates.Abilities(Api.Machine.Multiblock.PartAbility.MUFFLER)
				.SetExactLimit(1));
		return new BlockPattern(MultiblockShapes.LargeBoiler, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['C'] = casingOrExportFluids,
			['P'] = Predicates.Blocks(pipeTile),
			['X'] = fireboxOrInputs,
		});
	}

	// PatternFactory closure for `primitive_pump`. Mirror of
	// GTMultiMachines.PRIMITIVE_PUMP.pattern - 3 horizontal layers in upstream
	// (3D), collapsed to a single 3x4 face here. Char legend:
	//   S - controller (primitive_pump)
	//   X - pump_deck casing
	//   F - treated-wood frame
	//   H - PUMP_FLUID_HATCH ability (the pump_hatch part)
	// Upstream also accepts ULV/LV FLUID_EXPORT_HATCH on the H cell - dropped
	// here since the hatchModifier picks 1/2/4 off PumpHatch capacity, and our
	// tiered fluid hatches don't carry the PUMP_FLUID_HATCH ability token.
	private static IBlockPattern BuildPrimitivePumpPattern()
	{
		var def      = MachineRegistry.Get("primitive_pump")!;
		var treated  = Common.Materials.MaterialRegistry.Get("treated_wood")!;
		return new BlockPattern(MultiblockShapes.PrimitivePump, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.Blocks("pump_deck"),
			['F'] = Predicates.Frames(treated),
			['H'] = Predicates.Abilities(Api.Machine.Multiblock.PartAbility.PUMP_FLUID_HATCH),
		});
	}

	// PatternFactory closure for `primitive_blast_furnace`. Mirror of
	// GTMultiMachines.java:113-132 - every `X` cell must be firebricks
	// (CASING_PRIMITIVE_BRICKS). No optional I/O hatch (unlike coke_oven);
	// the controller's own 3 input / 3 output slots cover all I/O. The
	// upstream `&` (air-or-snow) and `#` (air) interior chimney cells collapse
	// away in our 2D shape - only the front face remains.
	private static IBlockPattern BuildPrimitiveBlastFurnacePattern()
	{
		var def = MachineRegistry.Get("primitive_blast_furnace")!;
		return new BlockPattern(MultiblockShapes.PrimitiveBlastFurnace, new Dictionary<char, TraceabilityPredicate>
		{
			['Y'] = Predicates.Controller(def),
			['X'] = Predicates.Blocks("firebricks"),
		});
	}

	// GCYMMachines.java:111-141. 'T' = TITANIUM_PIPE.
	private static IBlockPattern BuildLargeChemicalBathPattern()
	{
		var def = MachineRegistry.Get("large_chemical_bath")!;
		return new BlockPattern(MultiblockShapes.LargeChemicalBath, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("watertight_casing",
				new[] { GTRecipeTypes.CHEMICAL_BATH, GTRecipeTypes.ORE_WASHER }, parallel: true),
			['T'] = Predicates.Blocks("titanium_pipe_casing"),
		});
	}

	// GCYMMachines.java:84-109. 'G' = CRUSHING_WHEELS, 'A' = Air. Upstream's
	// LargeMacerationTowerMachine adds an entity-grind + item-suck zone inside
	// the structure (hurt non-item entities in the AABB, suck loose ItemEntity
	// into input buses); deliberately omitted in our port - recipe-driven
	// behavior is unaffected.
	private static IBlockPattern BuildLargeMacerationTowerPattern()
	{
		var def = MachineRegistry.Get("large_maceration_tower")!;
		return new BlockPattern(MultiblockShapes.LargeMacerationTower, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("secure_maceration_casing",
				new[] { GTRecipeTypes.MACERATOR }, parallel: true),
			['G'] = Predicates.Blocks("crushing_wheels"),
			['A'] = Predicates.Air(),
		});
	}

	// GCYMMachines.java:171-202. 'F' = HastelloyX frame (frameGt prefix), 'G' =
	// STAINLESS_STEEL_GEARBOX, 'P' = TITANIUM_PIPE. Upstream's LargeMixerMachine
	// attaches a MultiblockFluidRendererTrait (in-world fluid render of the
	// input hatches' contents); deliberately omitted - no 2D analogue.
	private static IBlockPattern BuildLargeMixerPattern()
	{
		var def       = MachineRegistry.Get("large_mixer")!;
		var hastelloy = Common.Materials.MaterialRegistry.Get("hastelloy_x")!;
		return new BlockPattern(MultiblockShapes.LargeMixer, new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.StandardWall("reaction_safe_mixing_casing",
				new[] { GTRecipeTypes.MIXER }, parallel: true),
			['F'] = Predicates.Frames(hastelloy),
			['G'] = Predicates.Blocks("stainless_steel_gearbox"),
			['P'] = Predicates.Blocks("titanium_pipe_casing"),
		});
	}

	// Register a tank-valve part. Mirrors GTMachineUtils.registerTankValve -
	// no recipe handling, no UI, just a I/O face that reskins to its host
	// tank's casing. `FusedCasingTileName` drives both the controller-side
	// part reskin AND the standalone (un-bound) item/tile rendering, so the
	// item icon already looks like the casing material before placement.
	private static void MultiblockTankValve(string id, string label, string casingTile)
	{
		MachineRegistry.Register(new MachineDefinition
		{
			Id = id, Label = label,
			Family = MachineFamily.TankValve,
			Tiered = false, Tiers = OneTier,
			// One valve belongs to one tank - no PartAbility token (pattern
			// matches the valve directly by tile name, not by ability).
			PartIo = IO.BOTH,
			PartFluidSlots = 1,
			// Same casing as the host tank - Predicates.Blocks(casing, valve)
			// expects the two tile sheets to be visually congruent.
			FusedCasingTileName = casingTile,
			OverlayDir          = "block/multiblock/tank_valve",
			OverlayBasename     = "overlay_front",
			LayoutKey           = "none",
		});
	}

	// Register a multiblock-tank controller. Storage capacity + per-material
	// containment filter come off `MachineDefinition.Capacity` + `MaterialId`;
	// `MultiblockTankMachine` reads both on first `EnsureTankTrait()`.
	private static void MultiblockTank(string id, string label, string? materialId,
		int capacity, string casingTile, string valveTile)
	{
		MachineRegistry.Register(new MachineDefinition
		{
			Id = id, Label = label,
			Family = MachineFamily.MultiblockTank,
			Tiered = false, Tiers = OneTier,
			// No recipe station - storage-only. Mirrors upstream
			// `recipeType(DUMMY_RECIPES)`.
			RecipeType = GTRecipeTypes.DUMMY,
			Capacity   = capacity,
			MaterialId = materialId,    // null for steel = no fluid-pipe filter
			FusedCasingTileName = casingTile,
			OverlayDir          = "block/multiblock/multiblock_tank",
			OverlayBasename     = "overlay_front",
			LayoutKey           = "multiblock_tank",
			PatternFactory      = () => BuildMultiblockTankPattern(id, casingTile, valveTile),
		});
	}

	// Shared pattern factory - same 3x3 shape for all three tiers, per-tier
	// (casing / valve) tile names bound at construction. Mirror of upstream
	// GTMachineUtils.registerMultiblockTank's .pattern(...) block. The wall
	// cell accepts the casing OR the valve; upstream's
	// `setMaxGlobalLimited(2, 0)` (at most 2 valves) is dropped here - the
	// `Predicates.Blocks(...)` OR-predicate has no global-limit hook today,
	// and an extra valve is harmless (each valve is an independent I/O face,
	// they all bind to the same single tank).
	private static IBlockPattern BuildMultiblockTankPattern(string id,
		string casingTile, string valveTile)
	{
		var def = MachineRegistry.Get(id)!;
		return new BlockPattern(MultiblockShapes.MultiblockTank,
			new Dictionary<char, TraceabilityPredicate>
		{
			['S'] = Predicates.Controller(def),
			['X'] = Predicates.Blocks(casingTile, valveTile),
		});
	}

	private static void Transformer(string id, string label, int baseAmp)
	{
		MachineRegistry.Register(new MachineDefinition
		{
			Id = id, Label = label,
			Family = MachineFamily.Transformer,
			Tiers = TransformerTiers,
			BaseAmp = baseAmp,
			LayoutKey = "none",
		});
	}
}
