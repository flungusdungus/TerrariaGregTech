#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Which behavioral entity class backs a machine. Each value maps to ONE
// registered ModTileEntity type - the collapse keeping tile-entity count
// under tML's 256 ceiling (TileEntitySharing truncates type >= 256).
// Machine identity WITHIN a family is data (MachineDefinition + VoltageTier),
// never a C# subclass.
public enum MachineFamily
{
	WorkableTiered,    // all recipe-driven processing machines
	SimpleGenerator,   // recipe-driven generators (steam turbine)
	SteamSolidBoiler,  // coal / hp-coal boilers
	SteamSolarBoiler,  // sunlight-powered boilers (lp / hp)
	SteamLiquidBoiler, // liquid-fuel boilers (lp / hp)
	SimpleSteam,       // steam-powered processing machines
	SteamMiner,        // SteamMinerMachine - steam-powered band-scan drill (lp/hp)
	BatteryBuffer,     // battery buffers + charger (charger = OutputAmps 0)
	Transformer,       // voltage transformers (1A/2A/4A/16A baseAmp variants)
	SolarPanel,        // passive generator
	Lamp,              // non-recipe consumer
	Fisher,            // FisherMachine - water-tile loot generator (LV..LuV)
	WorldAccelerator,  // WorldAcceleratorMachine - per-tile random-update accelerator over a square area.
	                   // BlockEntity-acceleration mode dropped (no Terraria analog).
	BlockBreaker,      // BlockBreakerMachine - dig-down drill. Range = 16 << tier (LV=32 ... ZPM=2048).
	                   // No cache (drops fall in-world), no facing.
	Miner,             // MinerMachine - tier-keyed WxD band drill (LV 16x500 -> EV 64x2000) with (tier+1)^2 cache.
	Pump,              // PumpMachine - bucket-fill-gate auto-pump. Two tanks (water + lava), 16 buckets x tier each.
	ItemCollector,     // ItemCollectorMachine - radius scan + pull into output cache, LV..EV.
	                   // Per-machine SimpleItemFilter + TagItemFilter (no upstream filter-item slot).
	Hull,              // tiered passthrough - crafting intermediate
	SuperTank,         // fluid storage
	SuperChest,        // item storage
	CreativeChest,     // CreativeChestMachine - infinite item source (InfiniteCache override of SuperChest).
	CreativeTank,      // CreativeTankMachine - infinite fluid source (InfiniteCache override of SuperTank).
	CreativeEnergy,    // CreativeEnergyContainerMachine - infinite EU source/sink, configurable V+A.
	Drum,              // per-material fluid storage
	Crate,             // per-material item storage

	// Multiblock controllers + parts - one family value per controller/part;
	// behaviour differs enough that one parameterised entity class isn't viable.
	MultiblockCokeOven,  // CokeOvenMachine - primitive multi: coal -> coke + creosote
	CokeOvenHatch,       // CokeOvenHatch - optional I/O face for coke_oven
	MultiblockPrimitiveBlastFurnace, // PrimitiveBlastFurnaceMachine - iron + fuel + igniter -> steel
	MultiblockPrimitivePump,         // PrimitivePumpMachine - biome-keyed water generator
	PumpHatch,                       // PumpHatchPartMachine - ULV water-filtered output hatch
	MultiblockLargeBoiler,           // LargeBoilerMachine - 4-tier steam producer

	// Tiered I/O parts - one entity per class, parameterised by MachineDefinition.
	ItemBus,             // ItemBusPartMachine - input_bus / output_bus x ALL_TIERS
	FluidHatch,          // FluidHatchPartMachine - 1x/4x/9x x MULTI_HATCH_TIERS, IN + OUT
	DualHatch,           // DualHatchPartMachine - item bus + fluid tank, LuV..MAX, IN + OUT
	EnergyHatch,         // EnergyHatchPartMachine - 2A/4A/16A/64A x tiers, IN + OUT
	Muffler,             // MufflerPartMachine - recovery-item exhaust x ELECTRIC_TIERS
	MaintenanceHatch,    // MaintenanceHatchPartMachine - LV plain / HV configurable
	AutoMaintenanceHatch,// AutoMaintenanceHatchPartMachine - full-auto, HV
	CleaningMaintenanceHatch, // CleaningMaintenanceHatchPartMachine - UV/UHV cleanroom-provider
	MultiblockCleanroom, // CleanroomMachine - enclosed cleanroom multi (5-15 wide)
	Diode,               // DiodePartMachine - one-way energy valve / passthrough hatch
	MultiblockEBF,       // ElectricBlastFurnaceMachine - coil-tier multi for high-temp recipes
	MultiblockElectricStandard, // shared WorkableElectricMultiblockMachine for standard processing multis
	MultiblockCoilStandard,     // shared CoilWorkableElectricMultiblockMachine; modifier supplied per-row via MultiRecipeModifier
	RotorHolder,         // RotorHolderPartMachine - turbine rotor slot, HV..UV
	ParallelHatch,       // ParallelHatchPartMachine - parallel-count UI part, IV..UV
	MultiblockDistillationTower, // DistillationTowerMachine - distillation_tower (per-layer routing) + large_distillery
	MultiblockAssemblyLine,      // AssemblyLineMachine - ordered-input multi (N-th bus = N-th input)
	MultiblockTank,              // MultiblockTankMachine - wooden / bronze / steel storage multi
	TankValve,                   // TankValvePartMachine - multi tank I/O face
	SteamHatch,                  // SteamHatchPartMachine - 64-bucket steam-filtered input
	MultiblockSteamParallel,     // SteamParallelMultiblockMachine - steam_grinder + steam_oven
	MultiblockLargeCombustionEngine, // LargeCombustionEngineMachine - large_combustion_engine + extreme_combustion_engine
	MultiblockLargeTurbine,          // LargeTurbineMachine - steam/gas/plasma turbine; output scales with rotor power x efficiency
	MultiblockLargeMiner,            // LargeMinerMachine - biome-keyed ore lottery, EV/IV/LuV (no chunk walker)
	MultiblockFluidDrillingRig,      // FluidDrillingRigMachine - biome-keyed fluid producer, MV/HV/EV
	MultiblockFusionReactor,         // FusionReactorMachine - 3-tier (LuV/ZPM/UV); own NEC capacitor + heat field;
	                                 // FUSION_OC pays eu_to_start from capacitor -> heat upfront.
	MultiblockActiveTransformer,     // ActiveTransformerMachine - power converter (UV); per-tick drain input/fill output.
	                                 // Structural failure while running explodes at `6 + tier`.
	LaserHatch,                      // LaserHatchPartMachine - {IN, OUT} x {256A, 1024A, 4096A}, IV..MAX.
	                                 // ILaserContainer; per-tick push through adjacent laser pipes.
	MultiblockPowerSubstation,       // PowerSubstationMachine - bulk EU storage (UV); PowerStationEnergyBank
	                                 // sums per-battery capacities (BigInteger); 20-tick transfer loop with passive drain.

	// Research / computation
	MultiblockHPCA,              // HPCAMachine - 3x3 component grid CWU/t provider with temperature/cooling/damage
	MultiblockDataBank,          // DataBankMachine - passive data hatch host (per-hatch EU upkeep)
	MultiblockNetworkSwitch,     // NetworkSwitchMachine - computation router / HPCA bridge
	MultiblockResearchStation,   // ResearchStationMachine - CWU + item + orb -> researched orb
	HpcaComponent,               // HPCAComponentPartMachine - collapsed 6-kind grid component (HpcaKind)
	DataAccessHatch,             // DataAccessHatchMachine - research-data store + recipe gate (4 tiers)
	ObjectHolder,                // ObjectHolderMachine - research station's item/orb pedestal
	OpticalComputationHatch,     // OpticalComputationHatchMachine - CWU transmitter/receiver
	OpticalDataHatch,            // OpticalDataHatchMachine - research-data transmitter/receiver

	// Long-distance pipeline endpoints (GUI-less, screwdriver Input/Output)
	LongDistanceItemEndpoint,    // LDItemEndpointMachine
	LongDistanceFluidEndpoint,   // LDFluidEndpointMachine
}
