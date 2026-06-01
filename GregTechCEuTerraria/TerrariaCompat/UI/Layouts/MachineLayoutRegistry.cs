#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Primitive;
using GregTechCEuTerraria.TerrariaCompat.Machine.Steam;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Layouts;

// Layout dispatch keyed on MachineDefinition.LayoutKey. "none" = no GUI.
public static class MachineLayoutRegistry
{
	public static MachineUILayout? Build(MetaMachine machine)
	{
		var def = machine.Definition;
		if (def == null) return null;

		return def.LayoutKey switch
		{
			"none"           => null,
			"steam_turbine"  => SteamTurbineLayout.Build((SimpleGeneratorMachine)machine),
			"coal_boiler"    => CoalBoilerLayout.Build((SteamSolidBoilerMachine)machine),
			"solar_boiler"   => SolarBoilerLayout.Build((SteamSolarBoiler)machine),
			"liquid_boiler"  => LiquidBoilerLayout.Build((SteamLiquidBoilerMachine)machine),
			"steam_machine"  => SteamMachineLayout.Build((SimpleSteamMachine)machine, machine.DisplayName),
			"super_tank"     => SuperTankLayout.Build((SuperTankTileEntity)machine),
			"super_chest"    => SuperChestLayout.Build((SuperChestTileEntity)machine),
			"creative_chest" => CreativeChestLayout.Build((TerrariaCompat.Tiles.Machines.Creative.CreativeChestTileEntity)machine),
			"creative_tank"  => CreativeTankLayout.Build((TerrariaCompat.Tiles.Machines.Creative.CreativeTankTileEntity)machine),
			"creative_energy" => CreativeEnergyLayout.Build((TerrariaCompat.Tiles.Machines.Creative.CreativeEnergyContainerMachine)machine),
			"drum"           => DrumLayout.Build((DrumMachine)machine),
			"crate"          => CrateLayout.Build((CrateMachine)machine),
			"battery_buffer" => ChargerMachineLayout.BuildGeneric(def.BatterySlotCount, machine.DisplayName),
			"fisher"         => FisherLayout.Build((FisherMachine)machine),
			"block_breaker"  => BlockBreakerLayout.Build((BlockBreakerMachine)machine),
			"miner"          => MinerLayout.Build((MinerMachine)machine),
			"steam_miner"    => SteamMinerLayout.Build((SteamMinerMachine)machine),
			"pump"           => PumpLayout.Build((PumpMachine)machine),
			"world_accelerator" => WorldAcceleratorLayout.Build((WorldAcceleratorMachine)machine),
			"item_collector" => ItemCollectorLayout.Build((ItemCollectorMachine)machine),
			"coke_oven"      => CokeOvenLayout.Build((CokeOvenMachine)machine),
			"primitive_blast_furnace" => PrimitiveBlastFurnaceLayout.Build((PrimitiveBlastFurnaceMachine)machine),
			"primitive_pump" => PrimitivePumpLayout.Build((PrimitivePumpMachine)machine),
			"large_boiler"   => LargeBoilerLayout.Build((TerrariaCompat.Machine.Multiblock.Steam.LargeBoilerMachine)machine),
			"multiblock_tank" => MultiblockTankLayout.Build((TerrariaCompat.Machine.Multiblock.MultiblockTankMachine)machine),
			"object_holder"  => ObjectHolderLayout.Build((ObjectHolderMachine)machine),
			"data_access"    => DataAccessHatchLayout.Build((DataAccessHatchMachine)machine),
			"item_bus"       => ItemBusLayout.Build((ItemBusPartMachine)machine),
			"fluid_hatch"    => FluidHatchLayout.Build((FluidHatchPartMachine)machine),
			"dual_hatch"     => DualHatchLayout.Build((DualHatchPartMachine)machine),
			"muffler"        => MufflerLayout.Build((MufflerPartMachine)machine),
			"rotor_holder"   => RotorHolderLayout.Build((RotorHolderPartMachine)machine),
			"parallel_hatch" => ParallelHatchLayout.Build((ParallelHatchPartMachine)machine),
			"maintenance"    => MaintenanceHatchLayout.Build((MaintenanceHatchPartMachine)machine),
			"cleanroom"      => CleanroomLayout.Build((TerrariaCompat.Machine.Multiblock.Electric.CleanroomMachine)machine),
			"power_substation" => PowerSubstationLayout.Build((TerrariaCompat.Machine.Multiblock.Electric.PowerSubstationMachine)machine),
			// HPCA / DataBank / NetworkSwitch - passive providers (no recipe,
			// two-state Idling/Providing display).
			"research_provider" => ResearchProviderLayout.Build((TerrariaCompat.Machine.Multiblock.WorkableElectricMultiblockMachine)machine),
			// Research Station - custom addDisplayText (research keys + CWU line).
			"research_station" => ResearchStationLayout.Build((TerrariaCompat.Machine.Multiblock.Electric.Research.ResearchStationMachine)machine),
			"generic_multi"  => GenericMultiblockLayout.Build((TerrariaCompat.Machine.Multiblock.WorkableElectricMultiblockMachine)machine),
			"steam_parallel_multi" => SteamParallelMultiblockLayout.Build((TerrariaCompat.Machine.Multiblock.Steam.SteamParallelMultiblockMachine)machine),
			_                => GenericMachineLayout.Build((WorkableTieredMachine)machine, machine.DisplayName),
		};
	}
}
