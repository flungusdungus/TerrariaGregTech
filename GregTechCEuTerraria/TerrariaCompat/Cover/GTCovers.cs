#nullable enable
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Cover.Filter;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Cover.Detector;
using GregTechCEuTerraria.TerrariaCompat.Cover.Ender;
using GregTechCEuTerraria.TerrariaCompat.Cover.Voiding;
using GregTechCEuTerraria.TerrariaCompat.Items.Covers;
using GregTechCEuTerraria.TerrariaCompat.Items.Registry;
using GregTechCEuTerraria.TerrariaCompat.Recipes;

namespace GregTechCEuTerraria.TerrariaCompat.Cover;

// Port of common.data.GTCovers - registers CoverDefinitions into CoverRegistry.
// Bare upstream ids (no "gtceu:" namespace); CoverItemLoader strips the namespace.
public static class GTCovers
{
	public static void Register()
	{
		// Verbatim SOLAR_PANEL_BASIC (1 EU/t).
		CoverRegistry.Register(new CoverDefinition("solar_panel",
			(def, coverable, side) => new CoverSolarPanel(def, coverable, side, 1)));

		// Verbatim registerTiered("solar_panel", ..., ALL_TIERS_WITH_ULV).
		for (var tier = VoltageTier.ULV; tier <= VoltageTier.UV; tier++)
		{
			long eut = VoltageTiers.Voltage(tier);
			string id = $"solar_panel.{VoltageTiers.Id(tier)}";
			CoverRegistry.Register(new CoverDefinition(id,
				(def, coverable, side) => new CoverSolarPanel(def, coverable, side, eut)));
		}

		CoverRegistry.Register(new CoverDefinition("infinite_water",
			(def, coverable, side) => new InfiniteWaterCover(def, coverable, side)));

		// One definition per tier LV->OpV (verbatim registerTiered).
		for (var tier = VoltageTier.LV; tier <= VoltageTier.OpV; tier++)
		{
			int t = (int)tier;
			string tierId = VoltageTiers.Id(tier);
			CoverRegistry.Register(new CoverDefinition($"conveyor.{tierId}",
				(def, coverable, side) => new ConveyorCover(def, coverable, side, t)));
			CoverRegistry.Register(new CoverDefinition($"pump.{tierId}",
				(def, coverable, side) => new PumpCover(def, coverable, side, t)));
			CoverRegistry.Register(new CoverDefinition($"robot_arm.{tierId}",
				(def, coverable, side) => new RobotArmCover(def, coverable, side, t)));
			CoverRegistry.Register(new CoverDefinition($"fluid_regulator.{tierId}",
				(def, coverable, side) => new FluidRegulatorCover(def, coverable, side, t)));
		}

		CoverRegistry.Register(new CoverDefinition("shutter",
			(def, coverable, side) => new ShutterCover(def, coverable, side)));

		// One CoverDefinition per filter family (item_filter / fluid_filter);
		// Simple / Tag / Smart are different cover ITEMS (same CoverPlaceBehavior,
		// different AttachItem). The cover's lazy filter-load dispatches via
		// FilterItemRegistry - verbatim `ItemFilter.FILTERS.get(attachItem)`.
		CoverRegistry.Register(new CoverDefinition("item_filter",
			(def, coverable, side) => new ItemFilterCover(def, coverable, side)));
		CoverRegistry.Register(new CoverDefinition("fluid_filter",
			(def, coverable, side) => new FluidFilterCover(def, coverable, side)));

		CoverRegistry.Register(new CoverDefinition("item_voiding",
			(def, coverable, side) => new ItemVoidingCover(def, coverable, side)));
		CoverRegistry.Register(new CoverDefinition("item_voiding_advanced",
			(def, coverable, side) => new AdvancedItemVoidingCover(def, coverable, side)));
		CoverRegistry.Register(new CoverDefinition("fluid_voiding",
			(def, coverable, side) => new FluidVoidingCover(def, coverable, side)));
		CoverRegistry.Register(new CoverDefinition("fluid_voiding_advanced",
			(def, coverable, side) => new AdvancedFluidVoidingCover(def, coverable, side)));

		// Rendering-only upstream; no-op here (2D, UI-only).
		CoverRegistry.Register(new CoverDefinition("facade",
			(def, coverable, side) => new FacadeCover(def, coverable, side)));

		// Detector covers - DetectorCover collapses upstream's analog 0-15 to
		// a binary wire pulse (Terraria wire has no level).
		CoverRegistry.Register(new CoverDefinition("maintenance_detector",
			(def, coverable, side) => new MaintenanceDetectorCover(def, coverable, side)));
		CoverRegistry.Register(new CoverDefinition("item_detector",
			(def, coverable, side) => new ItemDetectorCover(def, coverable, side)));
		CoverRegistry.Register(new CoverDefinition("item_detector_advanced",
			(def, coverable, side) => new AdvancedItemDetectorCover(def, coverable, side)));
		CoverRegistry.Register(new CoverDefinition("fluid_detector",
			(def, coverable, side) => new FluidDetectorCover(def, coverable, side)));
		CoverRegistry.Register(new CoverDefinition("fluid_detector_advanced",
			(def, coverable, side) => new AdvancedFluidDetectorCover(def, coverable, side)));
		CoverRegistry.Register(new CoverDefinition("energy_detector",
			(def, coverable, side) => new EnergyDetectorCover(def, coverable, side)));
		CoverRegistry.Register(new CoverDefinition("energy_detector_advanced",
			(def, coverable, side) => new AdvancedEnergyDetectorCover(def, coverable, side)));
		CoverRegistry.Register(new CoverDefinition("activity_detector",
			(def, coverable, side) => new ActivityDetectorCover(def, coverable, side)));
		CoverRegistry.Register(new CoverDefinition("activity_detector_advanced",
			(def, coverable, side) => new AdvancedActivityDetectorCover(def, coverable, side)));

		CoverRegistry.Register(new CoverDefinition("machine_controller",
			(def, coverable, side) => new MachineControllerCover(def, coverable, side)));

		// Ender link covers - virtual channels via VirtualEnderRegistry.
		CoverRegistry.Register(new CoverDefinition("ender_item_link",
			(def, coverable, side) => new EnderItemLinkCover(def, coverable, side)));
		CoverRegistry.Register(new CoverDefinition("ender_fluid_link",
			(def, coverable, side) => new EnderFluidLinkCover(def, coverable, side)));
		CoverRegistry.Register(new CoverDefinition("ender_redstone_link",
			(def, coverable, side) => new EnderRedstoneLinkCover(def, coverable, side)));

		CoverRegistry.Register(new CoverDefinition("storage",
			(def, coverable, side) => new StorageCover(def, coverable, side)));

		// Recipe-only placeholders - CanAttach=false, not implemented (no
		// Terraria analogue for either).
		CoverRegistry.Register(new CoverDefinition("computer_monitor",
			(def, coverable, side) => new ComputerMonitorCover(def, coverable, side)));
		CoverRegistry.Register(new CoverDefinition("wireless_transmitter",
			(def, coverable, side) => new WirelessTransmitterCover(def, coverable, side)));
	}

	// Populates FilterItemRegistry (verbatim ItemFilter.FILTERS /
	// FluidFilter.FILTERS) + wires tag-membership. Run AFTER CoverItemLoader /
	// RegistryItemLoader so filter items have ItemIDs. item_smart_filter
	// deferred - needs recipe-DB plumbing.
	public static void RegisterFilterItems()
	{
		// Cover items.
		if (CoverItemLoader.TryGet("gtceu:item_filter", out int itemFilterType))
			FilterItemRegistry.RegisterItemFilter(itemFilterType, SimpleItemFilter.LoadFilter);
		if (CoverItemLoader.TryGet("gtceu:fluid_filter", out int fluidFilterType))
			FilterItemRegistry.RegisterFluidFilter(fluidFilterType, SimpleFluidFilter.LoadFilter);

		// Inert filter-slot items (not covers) - resolve via IngredientResolver.
		int itemTagFilter = IngredientResolverImpl.Instance.ResolveItemType("gtceu:item_tag_filter");
		if (itemTagFilter > 0)
			FilterItemRegistry.RegisterItemFilter(itemTagFilter, TagItemFilter.LoadFilter);
		int fluidTagFilter = IngredientResolverImpl.Instance.ResolveItemType("gtceu:fluid_tag_filter");
		if (fluidTagFilter > 0)
			FilterItemRegistry.RegisterFluidFilter(fluidTagFilter, TagFluidFilter.LoadFilter);

		TagSource.ItemTags = TagMembership.ItemTagsOf;
		TagSource.FluidTags = TagMembership.FluidTagsOf;
	}
}
