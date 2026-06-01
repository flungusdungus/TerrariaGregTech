#nullable enable
using System;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Primitive;
using GregTechCEuTerraria.TerrariaCompat.Machine.Steam;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines.BatteryBuffers;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines.Transformers;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Resolves a MachineFamily to its single registered ModTileEntity prototype.
// Post-collapse there are ~8 family entity classes, each autoloaded once; a
// definition-driven tile uses this to Place the correct entity for its
// machine's family.
public static class MachineFamilyEntity
{
	public static MetaMachine For(MachineFamily family) => family switch
	{
		MachineFamily.WorkableTiered   => ModContent.GetInstance<WorkableTieredMachine>(),
		MachineFamily.SimpleGenerator  => ModContent.GetInstance<SimpleGeneratorMachine>(),
		MachineFamily.SteamSolidBoiler => ModContent.GetInstance<SteamSolidBoilerMachine>(),
		MachineFamily.SteamSolarBoiler => ModContent.GetInstance<SteamSolarBoiler>(),
		MachineFamily.SteamLiquidBoiler => ModContent.GetInstance<SteamLiquidBoilerMachine>(),
		MachineFamily.SimpleSteam      => ModContent.GetInstance<SimpleSteamMachine>(),
		MachineFamily.SteamMiner       => ModContent.GetInstance<Steam.SteamMinerMachine>(),
		MachineFamily.BatteryBuffer    => ModContent.GetInstance<BatteryBufferMachine>(),
		MachineFamily.Transformer      => ModContent.GetInstance<TransformerMachine>(),
		MachineFamily.SolarPanel       => ModContent.GetInstance<SolarPanelTileEntity>(),
		MachineFamily.Lamp             => ModContent.GetInstance<LampTileEntity>(),
		MachineFamily.Fisher           => ModContent.GetInstance<FisherMachine>(),
		MachineFamily.BlockBreaker     => ModContent.GetInstance<BlockBreakerMachine>(),
		MachineFamily.Miner            => ModContent.GetInstance<MinerMachine>(),
		MachineFamily.Pump             => ModContent.GetInstance<PumpMachine>(),
		MachineFamily.WorldAccelerator => ModContent.GetInstance<WorldAcceleratorMachine>(),
		MachineFamily.ItemCollector    => ModContent.GetInstance<ItemCollectorMachine>(),
		MachineFamily.Hull             => ModContent.GetInstance<HullMachine>(),
		MachineFamily.SuperTank        => ModContent.GetInstance<SuperTankTileEntity>(),
		MachineFamily.SuperChest       => ModContent.GetInstance<SuperChestTileEntity>(),
		MachineFamily.CreativeChest    => ModContent.GetInstance<Tiles.Machines.Creative.CreativeChestTileEntity>(),
		MachineFamily.CreativeTank     => ModContent.GetInstance<Tiles.Machines.Creative.CreativeTankTileEntity>(),
		MachineFamily.CreativeEnergy   => ModContent.GetInstance<Tiles.Machines.Creative.CreativeEnergyContainerMachine>(),
		MachineFamily.Drum             => ModContent.GetInstance<DrumMachine>(),
		MachineFamily.Crate            => ModContent.GetInstance<CrateMachine>(),
		MachineFamily.MultiblockCokeOven => ModContent.GetInstance<CokeOvenMachine>(),
		MachineFamily.CokeOvenHatch      => ModContent.GetInstance<CokeOvenHatch>(),
		MachineFamily.MultiblockPrimitiveBlastFurnace => ModContent.GetInstance<PrimitiveBlastFurnaceMachine>(),
		MachineFamily.MultiblockPrimitivePump         => ModContent.GetInstance<PrimitivePumpMachine>(),
		MachineFamily.PumpHatch                       => ModContent.GetInstance<PumpHatchPartMachine>(),
		MachineFamily.MultiblockLargeBoiler           => ModContent.GetInstance<Multiblock.Steam.LargeBoilerMachine>(),
		MachineFamily.ItemBus            => ModContent.GetInstance<ItemBusPartMachine>(),
		MachineFamily.FluidHatch         => ModContent.GetInstance<FluidHatchPartMachine>(),
		MachineFamily.DualHatch          => ModContent.GetInstance<DualHatchPartMachine>(),
		MachineFamily.EnergyHatch        => ModContent.GetInstance<EnergyHatchPartMachine>(),
		MachineFamily.Muffler            => ModContent.GetInstance<MufflerPartMachine>(),
		MachineFamily.MaintenanceHatch         => ModContent.GetInstance<MaintenanceHatchPartMachine>(),
		MachineFamily.AutoMaintenanceHatch     => ModContent.GetInstance<AutoMaintenanceHatchPartMachine>(),
		MachineFamily.CleaningMaintenanceHatch => ModContent.GetInstance<CleaningMaintenanceHatchPartMachine>(),
		MachineFamily.MultiblockCleanroom      => ModContent.GetInstance<Multiblock.Electric.CleanroomMachine>(),
		MachineFamily.Diode                    => ModContent.GetInstance<DiodePartMachine>(),
		MachineFamily.MultiblockEBF            => ModContent.GetInstance<Multiblock.Electric.ElectricBlastFurnaceMachine>(),
		MachineFamily.MultiblockElectricStandard => ModContent.GetInstance<Multiblock.WorkableElectricMultiblockMachine>(),
		MachineFamily.MultiblockCoilStandard     => ModContent.GetInstance<Multiblock.CoilWorkableElectricMultiblockMachine>(),
		MachineFamily.RotorHolder              => ModContent.GetInstance<RotorHolderPartMachine>(),
		MachineFamily.ParallelHatch            => ModContent.GetInstance<ParallelHatchPartMachine>(),
		MachineFamily.MultiblockDistillationTower => ModContent.GetInstance<Multiblock.Electric.DistillationTowerMachine>(),
		MachineFamily.MultiblockAssemblyLine     => ModContent.GetInstance<Multiblock.Electric.AssemblyLineMachine>(),
		MachineFamily.MultiblockTank             => ModContent.GetInstance<Multiblock.MultiblockTankMachine>(),
		MachineFamily.TankValve                  => ModContent.GetInstance<Multiblock.Part.TankValvePartMachine>(),
		MachineFamily.SteamHatch                 => ModContent.GetInstance<Multiblock.Part.SteamHatchPartMachine>(),
		MachineFamily.MultiblockSteamParallel    => ModContent.GetInstance<Multiblock.Steam.SteamParallelMultiblockMachine>(),
		MachineFamily.MultiblockLargeCombustionEngine => ModContent.GetInstance<Multiblock.Generator.LargeCombustionEngineMachine>(),
		MachineFamily.MultiblockLargeTurbine          => ModContent.GetInstance<Multiblock.Generator.LargeTurbineMachine>(),
		MachineFamily.MultiblockLargeMiner            => ModContent.GetInstance<Multiblock.Electric.LargeMinerMachine>(),
		MachineFamily.MultiblockFluidDrillingRig      => ModContent.GetInstance<Multiblock.Electric.FluidDrillingRigMachine>(),
		MachineFamily.MultiblockFusionReactor         => ModContent.GetInstance<Multiblock.Electric.FusionReactorMachine>(),
		MachineFamily.MultiblockActiveTransformer     => ModContent.GetInstance<Multiblock.Electric.ActiveTransformerMachine>(),
		MachineFamily.LaserHatch                      => ModContent.GetInstance<Multiblock.Part.LaserHatchPartMachine>(),
		MachineFamily.MultiblockPowerSubstation       => ModContent.GetInstance<Multiblock.Electric.PowerSubstationMachine>(),
		// Research / computation subsystem
		MachineFamily.MultiblockHPCA            => ModContent.GetInstance<Multiblock.Electric.Research.HPCAMachine>(),
		MachineFamily.MultiblockDataBank        => ModContent.GetInstance<Multiblock.Electric.Research.DataBankMachine>(),
		MachineFamily.MultiblockNetworkSwitch   => ModContent.GetInstance<Multiblock.Electric.Research.NetworkSwitchMachine>(),
		MachineFamily.MultiblockResearchStation => ModContent.GetInstance<Multiblock.Electric.Research.ResearchStationMachine>(),
		MachineFamily.HpcaComponent             => ModContent.GetInstance<Multiblock.Part.Hpca.HPCAComponentPartMachine>(),
		MachineFamily.DataAccessHatch           => ModContent.GetInstance<Multiblock.Part.DataAccessHatchMachine>(),
		MachineFamily.ObjectHolder              => ModContent.GetInstance<Multiblock.Part.ObjectHolderMachine>(),
		MachineFamily.OpticalComputationHatch   => ModContent.GetInstance<Multiblock.Part.OpticalComputationHatchMachine>(),
		MachineFamily.OpticalDataHatch          => ModContent.GetInstance<Multiblock.Part.OpticalDataHatchMachine>(),
		// Long-distance pipeline endpoints
		MachineFamily.LongDistanceItemEndpoint  => ModContent.GetInstance<Pipelike.LongDistance.LDItemEndpointMachine>(),
		MachineFamily.LongDistanceFluidEndpoint => ModContent.GetInstance<Pipelike.LongDistance.LDFluidEndpointMachine>(),
		_ => throw new ArgumentOutOfRangeException(nameof(family), family, "unmapped MachineFamily"),
	};
}
