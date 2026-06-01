#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

// =============================================================================
//  Hand-authored 2D footprint per multiblock controller. Edit freely.
// =============================================================================
//
//  Each entry mirrors upstream's `.aisle(...)` / `.where(...)` block (kept in a
//  comment) from `common/data/machines/{GTMultiMachines,GCYMMachines,
//  GTResearchMachines,GTMachineUtils}.java`; the `string[]` is the 2D footprint.
//
//  Tiered families that share a single pattern (large_boiler x4 casing
//  materials, fusion_reactor x3 tiers, etc.) collapse to ONE shape entry; the
//  concrete tier variants are listed in the entry's comment.
//
//  Char conventions (mirrored from upstream where they agree across multis;
//  per-multi overrides - including which letter is the controller - are in
//  each entry's `.where(...)` comment):
//    S / Y / O    controller block (one per shape)
//    X / H        casing
//    C            heating coil
//    P            inner pipe / catalyst
//    M            muffler-ability slot
//    #            air / hollow
//
//  Variable-size patterns (upstream `.setRepeatable(min, max)`) are declared
//  with the `RepeatableShape` record instead of a `string[]`:
//
//      public static readonly RepeatableShape DistillationTower = new(
//          Head:       new RowPattern[] { "XXXXX", "X#S#X" },   // top rows (fixed)
//          Body:       new RowPattern[] { "X###X" },             // repeated 0..10 times
//          Tail:       new RowPattern[] { "X###X", "XXXXX" },   // bottom rows (fixed)
//          MinVerticalRepeats: 0,
//          MaxVerticalRepeats: 10,
//          Axis:       RepeatAxis.Vertical);         // grows downward
//
//  Use `RepeatAxis.Horizontal` for assembly-line-style multis that grow
//  along the X axis instead - Head/Body/Tail then become left/middle/right
//  column-segments, each with the same row count. The matcher iterates
//  N = MinRepeats..MaxRepeats and picks the first N that matches the world.
//
//  Removed from the port (do NOT add entries for these): charcoal_pile_igniter,
//  central_monitor.
public static class MultiblockShapes
{
	// =========================================================================
	//  ActiveTransformer   (active_transformer)
	// =========================================================================
	//  Upstream source : GTMultiMachines.java
	//  Recipe type(s)  : DUMMY_RECIPES
	//  PORT NOTE:
	//    Stub-port: shape is declared but the runtime laser-cable pipeline
	//    //    (INPUT_LASER / OUTPUT_LASER part abilities) is not yet ported - the
	//    //    multi will register and form but its transfer logic is a no-op until
	//    //    laser cables land.
	//  Upstream pattern:
	// .aisle("XXX", "XXX", "XXX")
	// .aisle("XXX", "XCX", "XXX")
	// .aisle("XXX", "XSX", "XXX")
	// .where('S', controller(blocks(definition.getBlock())))
	// .where('X', blocks(GTBlocks.HIGH_POWER_CASING.get()) .or(ActiveTransformerMachine.getHatchPredicates()))
	// .where('C', blocks(GTBlocks.SUPERCONDUCTING_COIL.get()))
	public static readonly string[] ActiveTransformer =
	{
		"XXX",
		"XCX",
		"XSX",
	};

	// =========================================================================
	//  AlloyBlastSmelter   (alloy_blast_smelter)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : ALLOY_BLAST_RECIPES
	//  Upstream pattern:
	// .aisle("#XXX#", "#CCC#", "#GGG#", "#CCC#", "#XXX#")
	// .aisle("XXXXX", "CAAAC", "GAAAG", "CAAAC", "XXXXX")
	// .aisle("XXXXX", "CAAAC", "GAAAG", "CAAAC", "XXMXX")
	// .aisle("XXXXX", "CAAAC", "GAAAG", "CAAAC", "XXXXX")
	// .aisle("#XSX#", "#CCC#", "#GGG#", "#CCC#", "#XXX#")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_HIGH_TEMPERATURE_SMELTING.get()) .or(autoAbilities(definition.getRecipeTypes())) .or(Predicates.autoAbilities(true, false, false)))
	// .where('C', heatingCoils())
	// .where('M', abilities(PartAbility.MUFFLER))
	// .where('G', blocks(HEAT_VENT.get()))
	// .where('A', air())
	// .where('#', any())
	public static readonly string[] AlloyBlastSmelter =
	{
		"#XMX#",
		"XXXXX",
		"CCCCC",
		"XXXXX",
		"CCCCC",
		"XXSXX",
	};

	// =========================================================================
	//  AssemblyLine   (assembly_line)
	// =========================================================================
	//  Upstream source : GTMultiMachines.java
	//  Recipe type(s)  : ASSEMBLY_LINE_RECIPES
	//  Upstream pattern:
	// .aisle("FIF", "RTR", "SAG", "#Y#")
	// .aisle("FIF", "RTR", "DAG", "#Y#").setRepeatable(3, 15)
	// .aisle("FOF", "RTR", "DAG", "#Y#")
	// .where('S', Predicates.controller(blocks(definition.getBlock())))
	// .where('F', blocks(CASING_STEEL_SOLID.get()) .or(!ConfigHolder.INSTANCE.machines.orderedAssemblyLineFluids ? Predicates.abilities(PartAbility.IMPORT_FLUIDS_1X, PartAbility.IMPORT_FLUIDS_4X, PartAbility.IMPORT_FLUIDS_9X) : Predicates.abilities(PartAbility.IMPORT_FLUIDS_1X).setMaxGlobalLimited(4)))
	// .where('O', Predicates.abilities(PartAbility.EXPORT_ITEMS) .addTooltips(Component.translatable("gtceu.multiblock.pattern.location_end")))
	// .where('Y', blocks(CASING_STEEL_SOLID.get()).or(Predicates.abilities(PartAbility.INPUT_ENERGY) .setMaxGlobalLimited(2)))
	// .where('I', blocks(ITEM_IMPORT_BUS[0].getBlock()))
	// .where('G', blocks(CASING_GRATE.get()))
	// .where('A', blocks(CASING_ASSEMBLY_CONTROL.get()))
	// .where('R', blocks(CASING_LAMINATED_GLASS.get()))
	// .where('T', blocks(CASING_ASSEMBLY_LINE.get()))
	// .where('D', dataHatchPredicate(blocks(CASING_GRATE.get())))
	// .where('#', Predicates.any())
	// Upstream `setRepeatable(3, 15)` - assembly line grows along its long
	// axis. Your sketch stacks it as a vertical tower in our 2D world (one
	// "aisle" upstream = one row here).
	public static readonly RepeatableShape AssemblyLine = new(
		Head: new RowPattern[]
		{
			"YG",
			"SD",
			"FR",
			"FI",
		},
		Body: new RowPattern[]
		{
			"G",
			"D",
			"R",
			"I",
		},
		Tail: new RowPattern[]
		{
			"G",
			"D",
			"R",
			"O",
		},
		MinVerticalRepeats: 3,
		MaxVerticalRepeats: 15,
		Axis: RepeatAxis.Horizontal);

	// =========================================================================
	//  BedrockOreMiner   (bedrock_ore_miner)
	// =========================================================================
	//  Upstream source : GTMultiMachines.java
	//  Recipe type(s)  : DUMMY_RECIPES
	//  Concrete IDs    : <voltage_tier>_bedrock_ore_miner
	//  Upstream pattern:
	// .aisle("XXX", "#F#", "#F#", "#F#", "###", "###", "###")
	// .aisle("XXX", "FCF", "FCF", "FCF", "#F#", "#F#", "#F#")
	// .aisle("XSX", "#F#", "#F#", "#F#", "###", "###", "###")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(BedrockOreMinerMachine.getCasingState(tier)) .or(abilities(PartAbility.INPUT_ENERGY) .setMaxGlobalLimited(2)) .or(abilities(PartAbility.EXPORT_ITEMS).setMaxGlobalLimited(1)))
	// .where('C', blocks(BedrockOreMinerMachine.getCasingState(tier)))
	// .where('F', blocks(BedrockOreMinerMachine.getFrameState(tier)))
	// .where('#', any())
	public static readonly string[] BedrockOreMiner =
	{
		"#F#",
		"#F#",
		"#F#",
		"FCF",
		"FCF",
		"XXX",
		"XSX",
	};

	// =========================================================================
	//  Cleanroom   (cleanroom)
	// =========================================================================
	//  Upstream source : GTMultiMachines.java
	//  Recipe type(s)  : DUMMY_RECIPES
	//  PORT NOTE:
	//    Terraria-port plan: rectangle of blocks where the upper-centre block is
	//    //    the cleanroom controller, the rest of the TOP row is filter casings,
	//    //    and the remaining walls can be plascrete / cleanroom glass / any
	//    //    Terraria door. Sealed-room check enforces no gaps; recipes that
	//    //    require a cleanroom condition use the existing CleanroomCondition.
	//    //    Replaces upstream's parametric size - our shape is a fixed 2D rect.
	//  Upstream pattern:
	// .aisle("XXXXX", "XXXXX", "XXXXX", "XXXXX", "XXXXX")
	// .aisle("XXXXX", "X X", "X X", "X X", "XFFFX")
	// .aisle("XXXXX", "X X", "X X", "X X", "XFSFX")
	// .aisle("XXXXX", "X X", "X X", "X X", "XFFFX")
	// .aisle("XXXXX", "XXXXX", "XXXXX", "XXXXX", "XXXXX")
	// .where('X', blocks(GTBlocks.PLASTCRETE.get()) .or(blocks(GTBlocks.CLEANROOM_GLASS.get())) .or(abilities(PartAbility.PASSTHROUGH_HATCH).setMaxGlobalLimited(30, 3)) .or(abilities(PartAbility.INPUT_ENERGY).setMaxGlobalLimited(3, 2)) .or(blocks(ConfigHolder.INSTANCE.machines.enableMaintenance ? GTMachines.MAINTENANCE_HATCH.getBlock() : PLASTCRETE.get()).setExactLimit(1)) .or(blocks(Blocks.IRON_DOOR).setMaxGlobalLimited(8)))
	// .where('S', controller(blocks(definition.getBlock())))
	// .where(' ', any())
	// .where('E', abilities(PartAbility.INPUT_ENERGY))
	// .where('F', cleanroomFilters())
	// .where('I', abilities(PartAbility.PASSTHROUGH_HATCH))
	// Upstream cleanroom is parametric (side length 5-15) and scales on all
	// three axes. Our 2D port scales on BOTH width and height. Width is odd
	// 5-15: `HorizontalStep=2` walks {3,5,7,9,11,13} interior columns, so
	// width 1+N+1 in {5,7,9,11,13,15}. Height is any 5-15. Controller (S)
	// sits at the GEOMETRIC CENTRE of the ceiling row, surrounded by filter
	// (F) casings - the row materialises as `X + Fx(N/2) + S + Fx(N/2) + X`
	// via RowPattern's Center segment. Mirrors upstream `controller on
	// ceiling centre`.
	//
	// 11-wide x 5-tall example:
	//   XFFFFSFFFFX     <- ceiling: walls + Fx4 + S + Fx4 + walls
	//   X         X
	//   X         X     <- interior rows (vertical body repeats)
	//   X         X
	//   XXXXXXXXXXX     <- floor
	public static readonly RepeatableShape Cleanroom = new(
		Head: new RowPattern[]
		{
			new("X", "F", "X", Center: "S"),   // ceiling: X + Fx(N/2) + S + Fx(N/2) + X
		},
		Body: new RowPattern[]
		{
			new("X", " ", "X"),                // interior row: X + ' 'xN + X, repeats vertically
		},
		Tail: new RowPattern[]
		{
			new("X", "X", "X"),                // floor row: X + XxN + X (all-wall)
		},
		MinVerticalRepeats: 3,  MaxVerticalRepeats: 13,
		MinHorizontalRepeats: 3, MaxHorizontalRepeats: 13,
		HorizontalStep: 2);

	// =========================================================================
	//  CokeOven   (coke_oven)
	// =========================================================================
	//  Upstream source : GTMultiMachines.java
	//  Recipe type(s)  : COKE_OVEN_RECIPES
	//  Upstream pattern:
	// .aisle("XXX", "XXX", "XXX")
	// .aisle("XXX", "X#X", "XXX")
	// .aisle("XXX", "XYX", "XXX")
	// .where('X', blocks(CASING_COKE_BRICKS.get()).or(blocks(COKE_OVEN_HATCH.get()).setMaxGlobalLimited(5)))
	// .where('#', Predicates.air())
	// .where('Y', Predicates.controller(blocks(definition.getBlock())))
	public static readonly string[] CokeOven =
	{
		"XXX",
		"XYX",
		"XXX",
	};

	// =========================================================================
	//  Cracker   (cracker)
	// =========================================================================
	//  Upstream source : GTMultiMachines.java
	//  Recipe type(s)  : CRACKING_RECIPES
	//  Upstream pattern:
	// .aisle("HCHCH", "HCHCH", "HCHCH")
	// .aisle("HCHCH", "H###H", "HCHCH")
	// .aisle("HCHCH", "HCOCH", "HCHCH")
	// .where('O', Predicates.controller(blocks(definition.get())))
	// .where('H', blocks(CASING_STAINLESS_CLEAN.get()) .or(Predicates.autoAbilities(definition.getRecipeTypes())) .or(Predicates.autoAbilities(true, true, false)))
	// .where('#', Predicates.air())
	// .where('C', Predicates.heatingCoils())
	public static readonly string[] Cracker =
	{
		"HCHCH",
		"HCOCH",
		"HCHCH",
	};

	// =========================================================================
	//  DataBank   (data_bank)
	// =========================================================================
	//  Upstream source : GTResearchMachines.java
	//  Recipe type(s)  : DUMMY_RECIPES
	//  Upstream pattern:
	// .aisle("XDDDX", "XDDDX", "XDDDX")
	// .aisle("XDDDX", "XAAAX", "XDDDX")
	// .aisle("XCCCX", "XCSCX", "XCCCX")
	// .where('S', controller(blocks(definition.getBlock())))
	// .where('X', blocks(COMPUTER_HEAT_VENT.get()))
	// .where('D', blocks(COMPUTER_CASING.get()) .or(abilities(PartAbility.DATA_ACCESS).setPreviewCount(3)) .or(abilities(PartAbility.OPTICAL_DATA_TRANSMISSION)) .or(abilities(PartAbility.OPTICAL_DATA_RECEPTION).setPreviewCount(1)))
	// .where('A', blocks(COMPUTER_CASING.get()))
	// .where('C', blocks(HIGH_POWER_CASING.get()) .or(abilities(PartAbility.INPUT_ENERGY).setMaxGlobalLimited(2, 1)) .or(autoAbilities(true, false, false)))
	public static readonly string[] DataBank =
	{
		"XDDDX",
		"XDDDX",
		"XDSDX",
		"XDDDX",
		"XDDDX",
	};

	// =========================================================================
	//  DistillationTower   (distillation_tower)
	// =========================================================================
	//  Upstream source : GTMultiMachines.java
	//  Recipe type(s)  : DISTILLATION_RECIPES
	//  Upstream pattern:
	// .aisle("YSY", "YYY", "YYY")
	// .aisle("ZZZ", "Z#Z", "ZZZ")
	// .aisle("XXX", "X#X", "XXX").setRepeatable(0, 10)
	// .aisle("XXX", "XXX", "XXX")
	// .where('S', Predicates.controller(blocks(definition.getBlock())))
	// .where('Y', blocks(CASING_STAINLESS_CLEAN.get()) .or(Predicates.abilities(PartAbility.EXPORT_ITEMS).setMaxGlobalLimited(1)) .or(Predicates.abilities(PartAbility.INPUT_ENERGY) .setMaxGlobalLimited(2)) .or(Predicates.abilities(PartAbility.IMPORT_FLUIDS).setExactLimit(1)) .or(maint))
	// .where('Z', blocks(CASING_STAINLESS_CLEAN.get()) .or(exportPredicate) .or(maint))
	// .where('X', blocks(CASING_STAINLESS_CLEAN.get()).or(exportPredicate))
	// .where('#', Predicates.air())
	// Upstream `setRepeatable(0, 10)` - the X-aisle stacks vertically (the
	// tower's height). One upstream aisle = one row here. Drop the leading
	// "// " on the rows below to activate them.
	public static readonly RepeatableShape DistillationTower = new(
		Head: new RowPattern[]
		{
			"XXX",   // ceiling / top cap (export row)
		},
		Body: new RowPattern[]
		{
			"XAX",   // repeated body slice (output hatch row)
		},
		Tail: new RowPattern[]
		{
			"ZZZ",
			"YYY",
			"YSY",   // controller row at bottom
		},
		MinVerticalRepeats: 0,
		MaxVerticalRepeats: 10);

	// =========================================================================
	//  ElectricBlastFurnace   (electric_blast_furnace)
	// =========================================================================
	//  Upstream source : GTMultiMachines.java
	//  Recipe type(s)  : BLAST_RECIPES
	//  Upstream pattern:
	// .aisle("XXX", "CCC", "CCC", "XXX")
	// .aisle("XXX", "C#C", "C#C", "XMX")
	// .aisle("XSX", "CCC", "CCC", "XXX")
	// .where('S', controller(blocks(definition.getBlock())))
	// .where('X', blocks(CASING_INVAR_HEATPROOF.get()) .or(autoAbilities(definition.getRecipeTypes())) .or(autoAbilities(true, false, false)))
	// .where('M', abilities(PartAbility.MUFFLER))
	// .where('C', heatingCoils())
	// .where('#', air())
	public static readonly string[] ElectricBlastFurnace =
	{
		"XXMXX",
		"CCCCC",
		"CCCCC",
		"CCCCC",
		"XXSXX",
	};

	// =========================================================================
	//  FluidDrillingRig   (fluid_drilling_rig)
	// =========================================================================
	//  Upstream source : GTMultiMachines.java
	//  Recipe type(s)  : DUMMY_RECIPES
	//  Concrete IDs    : <voltage_tier>_fluid_drilling_rig
	//  Upstream pattern:
	// .aisle("XXX", "#F#", "#F#", "#F#", "###", "###", "###")
	// .aisle("XXX", "FCF", "FCF", "FCF", "#F#", "#F#", "#F#")
	// .aisle("XSX", "#F#", "#F#", "#F#", "###", "###", "###")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(FluidDrillMachine.getCasingState(tier)) .or(abilities(PartAbility.INPUT_ENERGY) .setMaxGlobalLimited(2)) .or(abilities(PartAbility.EXPORT_FLUIDS).setMaxGlobalLimited(1)))
	// .where('C', blocks(FluidDrillMachine.getCasingState(tier)))
	// .where('F', blocks(FluidDrillMachine.getFrameState(tier)))
	// .where('#', any())
	public static readonly string[] FluidDrillingRig =
	{
		"#F#",
		"#F#",
		"#F#",
		"FCF",
		"FCF",
		"XXX",
		"XSX",
	};

	// =========================================================================
	//  FusionReactor   (fusion_reactor)
	// =========================================================================
	//  Upstream source : GTMultiMachines.java
	//  Recipe type(s)  : FUSION_RECIPES
	//  Concrete IDs    : <voltage_tier>_fusion_reactor
	//  Upstream pattern:
	// .aisle("###############", "######OGO######", "###############")
	// .aisle("######ICI######", "####GGAAAGG####", "######ICI######")
	// .aisle("####CC###CC####", "###EAAOGOAAE###", "####CC###CC####")
	// .aisle("###C#######C###", "##EKEG###GEKE##", "###C#######C###")
	// .aisle("##C#########C##", "#GAE#######EAG#", "##C#########C##")
	// .aisle("##C#########C##", "#GAG#######GAG#", "##C#########C##")
	// .aisle("#I###########I#", "OAO#########OAO", "#I###########I#")
	// .aisle("#C###########C#", "GAG#########GAG", "#C###########C#")
	// .aisle("#I###########I#", "OAO#########OAO", "#I###########I#")
	// .aisle("##C#########C##", "#GAG#######GAG#", "##C#########C##")
	// .aisle("##C#########C##", "#GAE#######EAG#", "##C#########C##")
	// .aisle("###C#######C###", "##EKEG###GEKE##", "###C#######C###")
	// .aisle("####CC###CC####", "###EAAOGOAAE###", "####CC###CC####")
	// .aisle("######ICI######", "####GGAAAGG####", "######ICI######")
	// .aisle("###############", "######OSO######", "###############")
	// .where('S', controller(blocks(definition.get())))
	// .where('G', blocks(FUSION_GLASS.get()).or(casing))
	// .where('E', casing.or( blocks(PartAbility.INPUT_ENERGY.getBlockRange(tier, UV).toArray(Block[]::new)) .setPreviewCount(16)))
	// .where('C', casing)
	// .where('K', blocks(FusionReactorMachine.getCoilState(tier)))
	// .where('O', casing.or(abilities(PartAbility.EXPORT_FLUIDS)))
	// .where('A', air())
	// .where('I', casing.or(abilities(PartAbility.IMPORT_FLUIDS)))
	// .where('#', any())
	public static readonly string[] FusionReactor =
	{
		"######OGO######",
		"####IGAAAGI####",
		"###EAAOGOAAE###",
		"##EKEG###GEKE##",
		"#IAE#######EAI#",
		"#GAG#######GAG#",
		"OAO#########OAO",
		"GAG#########GAG",
		"OAO#########OAO",
		"#GAG#######GAG#",
		"#IAE#######EAI#",
		"##EKEG###GEKE##",
		"###EAAOGOAAE###",
		"####IGAAAGI####",
		"######OSO######",
	};

	// =========================================================================
	//  HighPerformanceComputationArray   (high_performance_computation_array)
	// =========================================================================
	//  Upstream source : GTResearchMachines.java
	//  Recipe type(s)  : DUMMY_RECIPES
	//  Upstream pattern:
	// .aisle("AA", "CC", "CC", "CC", "AA")
	// .aisle("VA", "XV", "XV", "XV", "VA")
	// .aisle("VA", "XV", "XV", "XV", "VA")
	// .aisle("VA", "XV", "XV", "XV", "VA")
	// .aisle("SA", "CC", "CC", "CC", "AA")
	// .where('S', controller(blocks(definition.getBlock())))
	// .where('A', blocks(ADVANCED_COMPUTER_CASING.get()))
	// .where('V', blocks(COMPUTER_HEAT_VENT.get()))
	// .where('X', abilities(PartAbility.HPCA_COMPONENT))
	// .where('C', blocks(COMPUTER_CASING.get()) .or(abilities(PartAbility.INPUT_ENERGY).setMaxGlobalLimited(2, 1)) .or(abilities(PartAbility.IMPORT_FLUIDS).setMaxGlobalLimited(1)) .or(abilities(PartAbility.COMPUTATION_DATA_TRANSMISSION).setExactLimit(1)) .or(autoAbilities(true, false, false)))
	public static readonly string[] HighPerformanceComputationArray =
	{
		"ACVCA",
		"AXXXA",
		"CXXXC",
		"AXXXA",
		"ACVCS",
	};

	// =========================================================================
	//  ImplosionCompressor   (implosion_compressor)
	// =========================================================================
	//  Upstream source : GTMultiMachines.java
	//  Recipe type(s)  : IMPLOSION_RECIPES
	//  Upstream pattern:
	// .aisle("XXX", "XXX", "XXX")
	// .aisle("XXX", "X#X", "XXX")
	// .aisle("XXX", "XSX", "XXX")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_STEEL_SOLID.get()) .or(Predicates.autoAbilities(definition.getRecipeTypes())) .or(Predicates.autoAbilities(true, true, false)))
	// .where('#', Predicates.air())
	public static readonly string[] ImplosionCompressor =
	{
		"XXX",
		"XXX",
		"XSX",
	};

	// =========================================================================
	//  LargeArcSmelter   (large_arc_smelter)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : ARC_FURNACE_RECIPES
	//  Upstream pattern:
	// .aisle("#XXX#", "#XXX#", "#XXX#", "#XXX#")
	// .aisle("XXXXX", "XCACX", "XCACX", "XXXXX")
	// .aisle("XXXXX", "XAAAX", "XAAAX", "XXMXX")
	// .aisle("XXXXX", "XACAX", "XACAX", "XXXXX")
	// .aisle("#XXX#", "#XSX#", "#XXX#", "#XXX#")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_HIGH_TEMPERATURE_SMELTING.get()) .or(Predicates.autoAbilities(definition.getRecipeTypes())) .or(Predicates.autoAbilities(true, false, true)))
	// .where('C', Predicates.blocks(MOLYBDENUM_DISILICIDE_COIL_BLOCK.get()))
	// .where('M', Predicates.abilities(MUFFLER))
	// .where('A', Predicates.air())
	// .where('#', Predicates.any())
	public static readonly string[] LargeArcSmelter =
	{
		"#XMX#",
		"XXXXX",
		"CCCCC",
		"XXXXX",
		"CCCCC",
		"XXSXX",
	};

	// =========================================================================
	//  LargeAssembler   (large_assembler)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : ASSEMBLER_RECIPES
	//  Upstream pattern:
	// .aisle("XXXXXXXXX", "XXXXXXXXX", "XXXXXXXXX")
	// .aisle("XXXXXXXXX", "XAAAXAAAX", "XGGGXXXXX")
	// .aisle("XXXXXXXXX", "XGGGXXSXX", "XGGGX###X")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_LARGE_SCALE_ASSEMBLING.get()) .or(Predicates.autoAbilities(definition.getRecipeTypes(), false, false, true, true, true, true)) .or(Predicates.abilities(INPUT_ENERGY).setExactLimit(1)) .or(Predicates.autoAbilities(true, false, true)))
	// .where('G', Predicates.blocks(CASING_TEMPERED_GLASS.get()))
	// .where('A', Predicates.air())
	// .where('#', Predicates.any())
	public static readonly string[] LargeAssembler =
	{
		"XXXXXXXXX",
		"XAAAAGSGX",
		"XXXXXXXXX",
	};

	// =========================================================================
	//  LargeAutoclave   (large_autoclave)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : AUTOCLAVE_RECIPES
	//  Upstream pattern:
	// .aisle("XXX", "XXX", "XXX")
	// .aisle("XXX", "XTX", "XXX")
	// .aisle("XXX", "XTX", "XXX")
	// .aisle("XXX", "XTX", "XXX")
	// .aisle("XXX", "XSX", "XXX")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_WATERTIGHT.get()) .or(autoAbilities(definition.getRecipeTypes())) .or(autoAbilities(true, false, true)))
	// .where('T', blocks(CASING_STEEL_PIPE.get()))
	// .where('#', any())
	public static readonly string[] LargeAutoclave =
	{
		"XXXXX",
		"XTTTS",
		"XXXXX",
	};

	// =========================================================================
	//  LargeBoiler   (large_boiler)
	// =========================================================================
	//  Upstream source : GTMachineUtils.java
	//  Recipe type(s)  : LARGE_BOILER_RECIPES
	//  Concrete IDs    : bronze_large_boiler, steel_large_boiler, titanium_large_boiler, tungstensteel_large_boiler
	//  Upstream pattern:
	// .aisle("XXX", "CCC", "CCC", "CCC")
	// .aisle("XXX", "CPC", "CPC", "CCC")
	// .aisle("XXX", "CSC", "CCC", "CCC")
	// .where('S', Predicates.controller(blocks(definition.getBlock())))
	// .where('P', blocks(pipe.get()))
	// .where('X', fireboxPred)
	// .where('C', blocks(casing.get()).setMinGlobalLimited(20)
	//      .or(Predicates.abilities(PartAbility.EXPORT_FLUIDS).setMinGlobalLimited(1).setPreviewCount(1)))
	//
	//  Upstream `fireboxPred` (GTMachineUtils.java:714-723) - the X-cell composition:
	//    blocks(firebox).setMinGlobalLimited(3)
	//      .or(abilities(IMPORT_FLUIDS).setMinGlobalLimited(1).setPreviewCount(1))
	//      .or(abilities(IMPORT_ITEMS).setMaxGlobalLimited(1).setPreviewCount(1))
	//      .or(abilities(MUFFLER).setExactLimit(1))
	//      .or(abilities(MAINTENANCE).setExactLimit(1))     // <- only if `enableMaintenance` config = true
	//
	//  Translation to our 2D shape: 6 X cells (XXX / XXX). Required counts:
	//  3x firebox + 1x IMPORT_FLUIDS + 1x MUFFLER = 5 mandatory; remaining
	//  1 cell can be IMPORT_ITEMS (solid fuel). MAINTENANCE branch dropped
	//  here (we don't ship the ConfigHolder system; `MaintenanceConfig.Enabled`
	//  defaults false - matches upstream's "config off" branch). See
	//  `BuildLargeBoilerPattern` in MachineDefinitions.cs.
	public static readonly string[] LargeBoiler =
	{
		"CCC",
		"CPC",
		"CSC",
		"XXX",
		"XXX",
	};

	// =========================================================================
	//  LargeBrewer   (large_brewer)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : BREWING_RECIPES, FERMENTING_RECIPES, FLUID_HEATER_RECIPES
	//  Upstream pattern:
	// .aisle("#XXX#", "#XXX#", "#XXX#", "#XXX#", "#####")
	// .aisle("XXXXX", "XCCCX", "XAAAX", "XXAXX", "##X##")
	// .aisle("XXXXX", "XCPCX", "XAPAX", "XAPAX", "#XXX#")
	// .aisle("XXXXX", "XCCCX", "XAAAX", "XXAXX", "##X##")
	// .aisle("#XXX#", "#XSX#", "#XXX#", "#XXX#", "#####")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_CORROSION_PROOF.get()) .or(autoAbilities(definition.getRecipeTypes())) .or(autoAbilities(true, false, true)))
	// .where('P', blocks(CASING_STEEL_PIPE.get()))
	// .where('C', blocks(MOLYBDENUM_DISILICIDE_COIL_BLOCK.get()))
	// .where('A', air())
	// .where('#', any())
	public static readonly string[] LargeBrewer =
	{
		"#XXX#",
		"XCCCX",
		"XCCCX",
		"XCSCX",
		"XXXXX",
	};

	// =========================================================================
	//  LargeCentrifuge   (large_centrifuge)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : CENTRIFUGE_RECIPES, THERMAL_CENTRIFUGE_RECIPES
	//  Upstream pattern:
	// .aisle("#XXX#", "XXXXX", "#XXX#")
	// .aisle("XXXXX", "XAPAX", "XXXXX")
	// .aisle("XXXXX", "XPAPX", "XXXXX")
	// .aisle("XXXXX", "XAPAX", "XXXXX")
	// .aisle("#XXX#", "XXSXX", "#XXX#")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_VIBRATION_SAFE.get()) .or(Predicates.autoAbilities(definition.getRecipeTypes())) .or(Predicates.autoAbilities(true, false, true)))
	// .where('P', Predicates.blocks(CASING_STEEL_PIPE.get()))
	// .where('A', Predicates.air())
	// .where('#', Predicates.any())
	public static readonly string[] LargeCentrifuge =
	{
		"#XXX#",
		"XXSXX",
		"#XXX#",
	};

	// =========================================================================
	//  LargeChemicalBath   (large_chemical_bath)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : CHEMICAL_BATH_RECIPES, ORE_WASHER_RECIPES
	//  Upstream pattern:
	// .aisle("XXXXX", "XXXXX", "XXXXX")
	// .aisle("XXXXX", "XTTTX", "X X")
	// .aisle("XXXXX", "X X", "X X")
	// .aisle("XXXXX", "X X", "X X")
	// .aisle("XXXXX", "X X", "X X")
	// .aisle("XXXXX", "XTTTX", "X X")
	// .aisle("XXXXX", "XXSXX", "XXXXX")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_WATERTIGHT.get()) .or(Predicates.autoAbilities(definition.getRecipeTypes())) .or(Predicates.autoAbilities(true, false, true)))
	// .where(' ', Predicates.air())
	// .where('T', Predicates.blocks(CASING_TITANIUM_PIPE.get()))
	public static readonly string[] LargeChemicalBath =
	{
		"XTTTX",
		"XXSXX",
		"XXXXX",
	};

	// =========================================================================
	//  LargeChemicalReactor   (large_chemical_reactor)
	// =========================================================================
	//  Upstream source : GTMultiMachines.java
	//  Recipe type(s)  : LARGE_CHEMICAL_RECIPES
	//  Upstream pattern:
	// .aisle("XXX", "XCX", "XXX")
	// .aisle("XCX", "CPC", "XCX")
	// .aisle("XXX", "XSX", "XXX")
	// .where('S', Predicates.controller(blocks(definition.getBlock())))
	// .where('X', casing.or(abilities))
	// .where('P', blocks(CASING_POLYTETRAFLUOROETHYLENE_PIPE.get()))
	// .where('C', Predicates.heatingCoils().setExactLimit(1) .or(abilities) .or(casing))
	public static readonly string[] LargeChemicalReactor =
	{
		"XXCXX",
		"XXXXX",
		"XXSXX",
		"XXXXX",
		"XXXXX",
	};

	// =========================================================================
	//  LargeCircuitAssembler   (large_circuit_assembler)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : CIRCUIT_ASSEMBLER_RECIPES
	//  Upstream pattern:
	// .aisle("XXXXXXX", "XXXXXXX", "XXXXXXX")
	// .aisle("XXXXXXX", "XPPPPPX", "XGGGGGX")
	// .aisle("XXXXXXX", "XAAAAPX", "XGGGGGX")
	// .aisle("XXXXXXX", "XTTTTXX", "XXXXXXX")
	// .aisle("#####XX", "#####SX", "#####XX")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_LARGE_SCALE_ASSEMBLING.get()) .or(Predicates.autoAbilities(definition.getRecipeTypes(), false, false, true, true, true, true)) .or(Predicates.abilities(INPUT_ENERGY).setExactLimit(1)) .or(Predicates.autoAbilities(true, false, true)))
	// .where('T', Predicates.blocks(CASING_TEMPERED_GLASS.get()))
	// .where('G', Predicates.blocks(CASING_GRATE.get()))
	// .where('P', blocks(CASING_TUNGSTENSTEEL_PIPE.get()))
	// .where('A', Predicates.air())
	// .where('#', Predicates.any())
	public static readonly string[] LargeCircuitAssembler =
	{
		"XXXXXXX",
		"XTGGPSX",
		"XXXXXXX",
	};

	// =========================================================================
	//  LargeCombustionEngine   (large_combustion_engine)
	// =========================================================================
	//  Upstream source : GTMachineUtils.java
	//  Recipe type(s)  : COMBUSTION_GENERATOR_FUELS
	//  Upstream pattern:
	// .aisle("XXX", "XDX", "XXX")
	// .aisle("XCX", "CGC", "XCX")
	// .aisle("XCX", "CGC", "XCX")
	// .aisle("AAA", "AYA", "AAA")
	// .where('X', blocks(casing.get()))
	// .where('G', blocks(gear.get()))
	// .where('C', blocks(casing.get()) .or(autoAbilities(definition.getRecipeTypes(), false, false, true, true, true, true)) .or(autoAbilities(true, true, false)))
	// .where('D', ability(PartAbility.OUTPUT_ENERGY, IntStream.of(ULV, LV, MV, HV, EV, IV, LuV, ZPM, UV, UHV) .filter(t -> t >= tier) .toArray()) .addTooltips(Component.translatable("gtceu.multiblock.pattern.error.limited.1", GTValues.VN[tier])))
	// .where('A', blocks(intake.get()) .addTooltips(Component.translatable("gtceu.multiblock.pattern.clear_amount_1")))
	// .where('Y', controller(blocks(definition.getBlock())))
	public static readonly string[] LargeCombustionEngine =
	{
		"XCCA",
		"DCCY",
		"XCCA",
	};

	// =========================================================================
	//  LargeCutter   (large_cutter)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : CUTTER_RECIPES, LATHE_RECIPES
	//  Upstream pattern:
	// .aisle("XXXXXXX", "XXXXXXX", "XXXXXXX", "##XXXXX")
	// .aisle("XXXXXXX", "XAXCCCX", "XXXAAAX", "##XXXXX")
	// .aisle("XXXXXXX", "XAXCCCX", "XXXAAAX", "##XXXXX")
	// .aisle("XXXXXXX", "XSXGGGX", "XXXGGGX", "##XXXXX")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_SHOCK_PROOF.get()) .or(autoAbilities(definition.getRecipeTypes())) .or(autoAbilities(true, false, true)))
	// .where('G', blocks(CASING_TEMPERED_GLASS.get()))
	// .where('C', blocks(SLICING_BLADES.get()))
	// .where('A', air())
	// .where('#', any())
	public static readonly string[] LargeCutter =
	{
		"XXXXXXX",
		"XGGGCCX",
		"XGGGXSX",
		"XXXXXXX",
	};

	// =========================================================================
	//  LargeDistillery   (large_distillery)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : DISTILLATION_RECIPES, DISTILLERY_RECIPES
	//  Upstream pattern:
	// .aisle("#YYY#", "YYYYY", "YYYYY", "YYYYY", "#YYY#")
	// .aisle("#YSY#", "YAAAY", "YAAAY", "YAAAY", "#YYY#")
	// .aisle("##X##", "#XAX#", "XAPAX", "#XAX#", "##X##").setRepeatable(1, 12)
	// .aisle("#####", "#ZZZ#", "#ZZZ#", "#ZZZ#", "#####")
	// .where('S', controller(blocks(definition.get())))
	// .where('Y', casingPredicate.or(abilities(IMPORT_ITEMS)) .or(abilities(INPUT_ENERGY).setMaxGlobalLimited(2)) .or(abilities(IMPORT_FLUIDS)) .or(abilities(EXPORT_ITEMS)) .or(autoAbilities(true, false, true)))
	// .where('X', casingPredicate.or(exportPredicate))
	// .where('Z', casingPredicate)
	// .where('P', blocks(CASING_STEEL_PIPE.get()))
	// .where('A', air())
	// .where('#', any())
	// Upstream `setRepeatable(1, 12)` - the X/P pipe-aisle stacks vertically.
	// One upstream aisle = one row here. Drop the leading "// " on the rows
	// below to activate them.
	public static readonly RepeatableShape LargeDistillery = new(
		Head: new RowPattern[]
		{
			"#ZZZ#",   // top cap (collection plate)
		},
		Body: new RowPattern[]
		{
			"XAPAX",   // repeated pipe slice
		},
		Tail: new RowPattern[]
		{
			"YAAAY",
			"YYSYY",   // controller row
			"YYYYY",
		},
		MinVerticalRepeats: 1,
		MaxVerticalRepeats: 12);

	// =========================================================================
	//  LargeElectrolyzer   (large_electrolyzer)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : ELECTROLYZER_RECIPES
	//  Upstream pattern:
	// .aisle("XXXXX", "XXXXX", "XXXXX")
	// .aisle("XXXXX", "XCCCX", "XCCCX")
	// .aisle("XXXXX", "XCCCX", "XCCCX")
	// .aisle("XXXXX", "XXSXX", "XXXXX")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_NONCONDUCTING.get()) .or(Predicates.autoAbilities(definition.getRecipeTypes())) .or(Predicates.autoAbilities(true, false, true)))
	// .where('C', blocks(ELECTROLYTIC_CELL.get()))
	public static readonly string[] LargeElectrolyzer =
	{
		"XXXXX",
		"XCCCX",
		"XCSCX",
		"XXXXX",
	};

	// =========================================================================
	//  LargeElectromagnet   (large_electromagnet)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : ELECTROMAGNETIC_SEPARATOR_RECIPES, POLARIZER_RECIPES
	//  Upstream pattern:
	// .aisle("XXXXX", "XXXXX", "XXXXX")
	// .aisle("XCXCX", "XCXCX", "XCXCX")
	// .aisle("XCXCX", "XCXCX", "XCXCX")
	// .aisle("XXXXX", "XXSXX", "XXXXX")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_NONCONDUCTING.get()) .or(Predicates.autoAbilities(definition.getRecipeTypes())) .or(Predicates.autoAbilities(true, false, true)))
	// .where('C', blocks(ELECTROLYTIC_CELL.get()))
	public static readonly string[] LargeElectromagnet =
	{
		"XXXXX",
		"XCXCX",
		"XCSCX",
		"XXXXX",
	};

	// =========================================================================
	//  LargeEngravingLaser   (large_engraving_laser)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : LASER_ENGRAVER_RECIPES
	//  Upstream pattern:
	// .aisle("XXXXX", "XXGXX", "XXGXX", "XXXXX")
	// .aisle("XXXXX", "XAAAX", "XAAAX", "XKKKX")
	// .aisle("XXXXX", "GAAAG", "GACAG", "XKXKX")
	// .aisle("XXXXX", "XAAAX", "XAAAX", "XKKKX")
	// .aisle("XXSXX", "XXGXX", "XXGXX", "XXXXX")
	// .where('S', controller(blocks(definition.get())))
	// .where('C', blocks(CASING_TUNGSTENSTEEL_PIPE.get()))
	// .where('X', blocks(CASING_LASER_SAFE_ENGRAVING.get()) .or(Predicates.autoAbilities(definition.getRecipeTypes())) .or(Predicates.autoAbilities(true, false, true)))
	// .where('G', blocks(CASING_TEMPERED_GLASS.get()))
	// .where('K', blocks(CASING_GRATE.get()))
	// .where('A', Predicates.air())
	public static readonly string[] LargeEngravingLaser =
	{
		"XXXXX",
		"XGGGX",
		"XGGGX",
		"XCCCX",
		"XXSXX",
	};

	// =========================================================================
	//  LargeExtractor   (large_extractor)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : EXTRACTOR_RECIPES, CANNER_RECIPES
	//  Upstream pattern:
	// .aisle("XXXXX", "XXXXX", "XXXXX")
	// .aisle("XXXXX", "XCACX", "XXXXX")
	// .aisle("XXXXX", "XXSXX", "XXXXX")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_WATERTIGHT.get()) .or(autoAbilities(definition.getRecipeTypes())) .or(autoAbilities(true, false, true)))
	// .where('C', blocks(CASING_STEEL_PIPE.get()))
	// .where('A', air())
	public static readonly string[] LargeExtractor =
	{
		"XXXXX",
		"XCSCX",
		"XXXXX",
	};

	// =========================================================================
	//  LargeExtruder   (large_extruder)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : EXTRUDER_RECIPES
	//  Upstream pattern:
	// .aisle("##XXX", "##XXX", "##XXX")
	// .aisle("##XXX", "##XPX", "##XGX").setRepeatable(2)
	// .aisle("XXXXX", "XXXPX", "XXXGX")
	// .aisle("XXXXX", "XAXPX", "XXXGX")
	// .aisle("XXXXX", "XSXXX", "XXXXX")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_STRESS_PROOF.get()) .or(autoAbilities(definition.getRecipeTypes())) .or(autoAbilities(true, false, true)))
	// .where('P', blocks(CASING_TITANIUM_PIPE.get()))
	// .where('G', blocks(CASING_TEMPERED_GLASS.get()))
	// .where('A', air())
	// .where('#', any())
	public static readonly string[] LargeExtruder =
	{
		"XXXXX",
		"XGGSX",
		"XXXXX",
	};

	// =========================================================================
	//  LargeMacerationTower   (large_maceration_tower)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : MACERATOR_RECIPES
	//  Upstream pattern:
	// .aisle("XXXXX", "XXXXX", "XXXXX", "XXXXX")
	// .aisle("XXXXX", "XGGGX", "XGGGX", "XAAAX")
	// .aisle("XXXXX", "XGGGX", "XGGGX", "XAAAX")
	// .aisle("XXXXX", "XGGGX", "XGGGX", "XAAAX")
	// .aisle("XXXXX", "XXXXX", "XXSXX", "XXXXX")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_SECURE_MACERATION.get()) .or(Predicates.autoAbilities(definition.getRecipeTypes())) .or(Predicates.autoAbilities(true, false, true)))
	// .where('G', Predicates.blocks(CRUSHING_WHEELS.get()))
	// .where('A', Predicates.air())
	public static readonly string[] LargeMacerationTower =
	{
		"XXXXX",
		"XXXXX",
		"XGSGX",
		"XGGGX",
		"XXXXX",
	};

	// =========================================================================
	//  LargeMaterialPress   (large_material_press)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : BENDER_RECIPES, COMPRESSOR_RECIPES, FORGE_HAMMER_RECIPES, FORMING_PRESS_RECIPES
	//  Upstream pattern:
	// .aisle("XXXXXXX", "XXXXXXX", "XXXXXXX")
	// .aisle("XXXXXXX", "XAXGGGX", "XXXXXXX")
	// .aisle("XXXXXXX", "XSXCCCX", "XXXXXXX")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_STRESS_PROOF.get()) .or(autoAbilities(definition.getRecipeTypes())) .or(autoAbilities(true, false, true)))
	// .where('G', blocks(CASING_STEEL_GEARBOX.get()))
	// .where('C', blocks(CASING_TEMPERED_GLASS.get()))
	// .where('A', air())
	public static readonly string[] LargeMaterialPress =
	{
		"XXXXXXX",
		"XCCCGSX",
		"XXXXXXX",
	};

	// =========================================================================
	//  LargeMiner   (large_miner)
	// =========================================================================
	//  Upstream source : GTMultiMachines.java
	//  Recipe type(s)  : MACERATOR_RECIPES
	//  Concrete IDs    : <voltage_tier>_large_miner
	//  Upstream pattern:
	// .aisle("XXX", "#F#", "#F#", "#F#", "###", "###", "###")
	// .aisle("XXX", "FCF", "FCF", "FCF", "#F#", "#F#", "#F#")
	// .aisle("XSX", "#F#", "#F#", "#F#", "###", "###", "###")
	// .where('S', controller(blocks(definition.getBlock())))
	// .where('X', blocks(LargeMinerMachine.getCasingState(tier)) .or(abilities(PartAbility.EXPORT_ITEMS).setExactLimit(1).setPreviewCount(1)) .or(abilities(PartAbility.IMPORT_FLUIDS).setExactLimit(1).setPreviewCount(1)) .or(abilities(PartAbility.INPUT_ENERGY) .setMaxGlobalLimited(2).setPreviewCount(1)))
	// .where('C', blocks(LargeMinerMachine.getCasingState(tier)))
	// .where('F', frames(LargeMinerMachine.getMaterial(tier)))
	// .where('#', any())
	public static readonly string[] LargeMiner =
	{
		"#F#",
		"#F#",
		"#F#",
		"FCF",
		"FCF",
		"FCF",
		"XXX",
		"XSX",
		"XXX",
	};

	// =========================================================================
	//  LargeMixer   (large_mixer)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : MIXER_RECIPES
	//  Upstream pattern:
	// .aisle("#XXX#", "#XXX#", "#XXX#", "#XXX#", "#XXX#", "##F##")
	// .aisle("XXXXX", "XAPAX", "XAAAX", "XAPAX", "XAAAX", "##F##")
	// .aisle("XXXXX", "XPPPX", "XAPAX", "XPPPX", "XAGAX", "FFGFF")
	// .aisle("XXXXX", "XAPAX", "XAAAX", "XAPAX", "XAAAX", "##F##")
	// .aisle("#XXX#", "#XSX#", "#XXX#", "#XXX#", "#XXX#", "##F##")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_REACTION_SAFE.get()) .or(autoAbilities(definition.getRecipeTypes())) .or(Predicates.autoAbilities(true, false, true)))
	// .where('F', blocks(ChemicalHelper.getBlock(TagPrefix.frameGt, GTMaterials.HastelloyX)))
	// .where('G', blocks(CASING_STAINLESS_STEEL_GEARBOX.get()))
	// .where('P', blocks(CASING_TITANIUM_PIPE.get()))
	// .where('A', Predicates.air())
	// .where('#', Predicates.any())
	public static readonly string[] LargeMixer =
	{
		"FFGFF",
		"XXPXX",
		"XXPXX",
		"XXSXX",
		"XXXXX",
	};

	// =========================================================================
	//  LargePacker   (large_packer)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : PACKER_RECIPES
	//  Upstream pattern:
	// .aisle("XXX", "XXX", "XXX")
	// .aisle("XXX", "XAX", "XXX")
	// .aisle("XXX", "XAX", "XXX")
	// .aisle("XXX", "XAX", "XXX")
	// .aisle("XXX", "XAX", "XXX")
	// .aisle("XXX", "XSX", "XXX")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_TUNGSTENSTEEL_ROBUST.get()) .or(Predicates.autoAbilities(definition.getRecipeTypes())) .or(Predicates.autoAbilities(true, false, true)))
	// .where('A', Predicates.air())
	public static readonly string[] LargePacker =
	{
		"XXXXXX",
		"XAAAAS",
		"XXXXXX",
	};

	// =========================================================================
	//  LargeSiftingFunnel   (large_sifting_funnel)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : SIFTER_RECIPES
	//  Upstream pattern:
	// .aisle("#X#X#", "#X#X#", "#XXX#", "XXXXX", "#XXX#")
	// .aisle("XXXXX", "XAXAX", "XKKKX", "XKKKX", "X###X")
	// .aisle("#XXX#", "#XAX#", "XKKKX", "XKKKX", "X###X")
	// .aisle("XXXXX", "XAXAX", "XKKKX", "XKKKX", "X###X")
	// .aisle("#X#X#", "#X#X#", "#XSX#", "XXXXX", "#XXX#")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_VIBRATION_SAFE.get()) .or(Predicates.autoAbilities(definition.getRecipeTypes())) .or(Predicates.autoAbilities(true, false, true)))
	// .where('K', blocks(CASING_GRATE.get()))
	// .where('A', Predicates.air())
	// .where('#', Predicates.any())
	public static readonly string[] LargeSiftingFunnel =
	{
		"#XXX#",
		"XAAAX",
		"XKKKX",
		"XKSKX",
		"XXXXX",
	};

	// =========================================================================
	//  LargeSolidifier   (large_solidifier)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : FLUID_SOLIDFICATION_RECIPES
	//  Upstream pattern:
	// .aisle("#XXX#", "#XXX#", "#XXX#", "#XXX#")
	// .aisle("XXXXX", "XCACX", "XCACX", "XXXXX")
	// .aisle("XXXXX", "XAAAX", "XAAAX", "XXXXX")
	// .aisle("XXXXX", "XCACX", "XCACX", "XXXXX")
	// .aisle("#XXX#", "#XSX#", "#XXX#", "#XXX#")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_WATERTIGHT.get()) .or(autoAbilities(definition.getRecipeTypes())) .or(autoAbilities(true, false, true)))
	// .where('C', blocks(CASING_STEEL_PIPE.get()))
	// .where('A', air())
	// .where('#', any())
	public static readonly string[] LargeSolidifier =
	{
		"#XXX#",
		"XAAAX",
		"XAAAX",
		"XCSCX",
		"XXXXX",
	};

	// =========================================================================
	//  LargeTurbine   (large_turbine)
	// =========================================================================
	//  Upstream source : GTMachineUtils.java
	//  Recipe type(s)  : recipeType
	//  Concrete IDs    : steam_large_turbine, gas_large_turbine, plasma_large_turbine
	//  Upstream pattern:
	// .aisle("CCCC", "CHHC", "CCCC")
	// .aisle("CHHC", "RGGR", "CHHC")
	// .aisle("CCCC", "CSHC", "CCCC")
	// .where('S', controller(blocks(definition.getBlock())))
	// .where('G', blocks(gear.get()))
	// .where('C', blocks(casing.get()))
	// .where('R', new TraceabilityPredicate( new SimplePredicate( state -> MetaMachine.getMachine(state.getWorld(), state.getPos()) instanceof RotorHolderPartMachine rotorHolder && state.getWorld() .getBlockState(state.getPos() .relative(rotorHolder.self().getFrontFacing())) .isAir(), () -> PartAbility.ROTOR_HOLDER.getAllBlocks().stream() .map(BlockInfo::fromBlock).toArray(BlockInfo[]::new))) .addTooltips(Component.translatable("gtceu.multiblock.pattern.clear_amount_3")) .addTooltips(Component.translatable("gtceu.multiblock.pattern.error.limited.1", VN[tier])) .setExactLimit(1) .or(abilities(PartAbility.OUTPUT_ENERGY)).setExactLimit(1))
	// .where('H', blocks(casing.get()) .or(autoAbilities(definition.getRecipeTypes(), false, false, true, true, true, true)) .or(autoAbilities(true, needsMuffler, false)))
	public static readonly string[] LargeTurbine =
	{
		"CHHC",
		"RHSR",
		"CHHC",
	};

	// =========================================================================
	//  LargeWiremill   (large_wiremill)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : WIREMILL_RECIPES
	//  Upstream pattern:
	// .aisle("XXXXX", "XXXXX", "XXX##")
	// .aisle("XXXXX", "X#CCX", "XXXXX")
	// .aisle("XXXXX", "XSXXX", "XXX##")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_STRESS_PROOF.get()) .or(autoAbilities(definition.getRecipeTypes())) .or(autoAbilities(true, false, true)))
	// .where('C', blocks(CASING_TITANIUM_GEARBOX.get()))
	// .where('#', any())
	public static readonly string[] LargeWiremill =
	{
		"XXXXX",
		"XCCSX",
		"XXXXX",
	};

	// =========================================================================
	//  MegaBlastFurnace   (mega_blast_furnace)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : BLAST_RECIPES
	//  Upstream pattern:
	// .aisle("##XXXXXXXXX##", "##XXXXXXXXX##", "#############", "#############", "#############", "#############", "#############", "#############", "#############", "#############", "#############", "#############", "#############", "#############", "#############", "#############", "#############")
	// .aisle("#XXXXXXXXXXX#", "#XXXXXXXXXXX#", "###F#####F###", "###F#####F###", "###FFFFFFF###", "#############", "#############", "#############", "#############", "#############", "####FFFFF####", "#############", "#############", "#############", "#############", "#############", "#############")
	// .aisle("XXXXXXXXXXXXX", "XXXXVVVVVXXXX", "##F#######F##", "##F#######F##", "##FFFHHHFFF##", "##F#######F##", "##F#######F##", "##F#######F##", "##F#######F##", "##F#######F##", "##FFFHHHFFF##", "#############", "#############", "#############", "#############", "#############", "###TTTTTTT###")
	// .aisle("XXXXXXXXXXXXX", "XXXXXXXXXXXXX", "#F####P####F#", "#F####P####F#", "#FFHHHPHHHFF#", "######P######", "######P######", "######P######", "######P######", "######P######", "##FHHHPHHHF##", "######P######", "######P######", "######P######", "######P######", "######P######", "##TTTTPTTTT##")
	// .aisle("XXXXXXXXXXXXX", "XXVXXXXXXXVXX", "####BBPBB####", "####TITIT####", "#FFHHHHHHHFF#", "####BITIB####", "####CCCCC####", "####CCCCC####", "####CCCCC####", "####BITIB####", "#FFHHHHHHHFF#", "####BITIB####", "####CCCCC####", "####CCCCC####", "####CCCCC####", "####BITIB####", "##TTTTPTTTT##")
	// .aisle("XXXXXXXXXXXXX", "XXVXXXXXXXVXX", "####BAAAB####", "####IAAAI####", "#FHHHAAAHHHF#", "####IAAAI####", "####CAAAC####", "####CAAAC####", "####CAAAC####", "####IAAAI####", "#FHHHAAAHHHF#", "####IAAAI####", "####CAAAC####", "####CAAAC####", "####CAAAC####", "####IAAAI####", "##TTTTPTTTT##")
	// .aisle("XXXXXXXXXXXXX", "XXVXXXXXXXVXX", "###PPAAAPP###", "###PTAAATP###", "#FHPHAAAHPHF#", "###PTAAATP###", "###PCAAACP###", "###PCAAACP###", "###PCAAACP###", "###PTAAATP###", "#FHPHAAAHPHF#", "###PTAAATP###", "###PCAAACP###", "###PCAAACP###", "###PCAAACP###", "###PTAAATP###", "##TPPPMPPPT##")
	// .aisle("XXXXXXXXXXXXX", "XXVXXXXXXXVXX", "####BAAAB####", "####IAAAI####", "#FHHHAAAHHHF#", "####IAAAI####", "####CAAAC####", "####CAAAC####", "####CAAAC####", "####IAAAI####", "#FHHHAAAHHHF#", "####IAAAI####", "####CAAAC####", "####CAAAC####", "####CAAAC####", "####IAAAI####", "##TTTTPTTTT##")
	// .aisle("XXXXXXXXXXXXX", "XXVXXXXXXXVXX", "####BBPBB####", "####TITIT####", "#FFHHHHHHHFF#", "####BITIB####", "####CCCCC####", "####CCCCC####", "####CCCCC####", "####BITIB####", "#FFHHHHHHHFF#", "####BITIB####", "####CCCCC####", "####CCCCC####", "####CCCCC####", "####BITIB####", "##TTTTPTTTT##")
	// .aisle("XXXXXXXXXXXXX", "XXXXXXXXXXXXX", "#F####P####F#", "#F####P####F#", "#FFHHHPHHHFF#", "######P######", "######P######", "######P######", "######P######", "######P######", "##FHHHPHHHF##", "######P######", "######P######", "######P######", "######P######", "######P######", "##TTTTPTTTT##")
	// .aisle("XXXXXXXXXXXXX", "XXXXVVVVVXXXX", "##F#######F##", "##F#######F##", "##FFFHHHFFF##", "##F#######F##", "##F#######F##", "##F#######F##", "##F#######F##", "##F#######F##", "##FFFHHHFFF##", "#############", "#############", "#############", "#############", "#############", "###TTTTTTT###")
	// .aisle("#XXXXXXXXXXX#", "#XXXXXXXXXXX#", "###F#####F###", "###F#####F###", "###FFFFFFF###", "#############", "#############", "#############", "#############", "#############", "####FFFFF####", "#############", "#############", "#############", "#############", "#############", "#############")
	// .aisle("##XXXXXXXXX##", "##XXXXSXXXX##", "#############", "#############", "#############", "#############", "#############", "#############", "#############", "#############", "#############", "#############", "#############", "#############", "#############", "#############", "#############")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', casing.or(autoAbilities(definition.getRecipeTypes())) .or(Predicates.autoAbilities(true, false, true)))
	// .where('C', heatingCoils())
	// .where('M', abilities(PartAbility.MUFFLER))
	// .where('F', blocks(ChemicalHelper.getBlock(TagPrefix.frameGt, NaquadahAlloy)))
	// .where('H', casing)
	// .where('T', blocks(CASING_TUNGSTENSTEEL_ROBUST.get()))
	// .where('B', blocks(FIREBOX_TUNGSTENSTEEL.get()))
	// .where('P', blocks(CASING_TUNGSTENSTEEL_PIPE.get()))
	// .where('I', blocks(CASING_EXTREME_ENGINE_INTAKE.get()))
	// .where('V', blocks(HEAT_VENT.get()))
	// .where('A', air())
	// .where('#', any())
	public static readonly string[] MegaBlastFurnace =
	{
		"XXXXXBMBXXXXX",
		"XCCCCCCCCCCCX",
		"XCCCCCCCCCCCX",
		"XCCCCCCCCCCCX",
		"XCCCCCCCCCCCX",
		"XCCCCCCCCCCCX",
		"XCCCCCCCCCCCX",
		"XCCCCCCCCCCCX",
		"XCCCCCCCCCCCX",
		"XCCCCCCCCCCCX",
		"XCCCCCCCCCCCX",
		"XFFFFFFFFFFFX",
		"XXXXXBSBXXXXX",
	};

	// =========================================================================
	//  MegaVacuumFreezer   (mega_vacuum_freezer)
	// =========================================================================
	//  Upstream source : GCYMMachines.java
	//  Recipe type(s)  : VACUUM_RECIPES
	//  Upstream pattern:
	// .aisle("XXXXXXX#KKK", "XXXXXXX#KVK", "XXXXXXX#KVK", "XXXXXXX#KVK", "XXXXXXX#KKK", "XXXXXXX####", "XXXXXXX####")
	// .aisle("XXXXXXX#KVK", "XPPPPPPPPPV", "XPAPAPX#VPV", "XPPPPPPPPPV", "XPAPAPX#KVK", "XPPPPPX####", "XXXXXXX####")
	// .aisle("XXXXXXX#KVK", "XPAPAPX#VPV", "XAAAAAX#VPV", "XPAAAPX#VPV", "XAAAAAX#KVK", "XPAPAPX####", "XXXXXXX####")
	// .aisle("XXXXXXX#KVK", "XPAPAPPPPPV", "XAAAAAX#VPV", "XPAAAPPPPPV", "XAAAAAX#KVK", "XPAPAPX####", "XXXXXXX####")
	// .aisle("XXXXXXX#KKK", "XPPPPPX#KVK", "XPA#APX#KVK", "XPAAAPX#KVK", "XPAAAPX#KKK", "XPPPPPX####", "XXXXXXX####")
	// .aisle("#XXXXX#####", "#XXSXX#####", "#XGGGX#####", "#XGGGX#####", "#XGGGX#####", "#XXXXX#####", "###########")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_ALUMINIUM_FROSTPROOF.get()) .or(autoAbilities(definition.getRecipeTypes())) .or(Predicates.autoAbilities(true, false, true)))
	// .where('G', blocks(CASING_TEMPERED_GLASS.get()))
	// .where('K', blocks(CASING_STAINLESS_CLEAN.get()))
	// .where('P', blocks(CASING_TUNGSTENSTEEL_PIPE.get()))
	// .where('V', blocks(HEAT_VENT.get()))
	// .where('A', air())
	// .where('#', any())
	public static readonly string[] MegaVacuumFreezer =
	{
		"XXXXXXX#KKK",
		"XAAAAAX#KPK",
		"XAAAAAX#KPK",
		"XAAAAAX#KPK",
		"XPPSPPPPPPK",
		"XXXXXXX#KKK",
	};

	// =========================================================================
	//  MultiSmelter   (multi_smelter)
	// =========================================================================
	//  Upstream source : GTMultiMachines.java
	//  Recipe type(s)  : FURNACE_RECIPES, ALLOY_SMELTER_RECIPES
	//  Upstream pattern:
	// .aisle("XXX", "CCC", "XXX")
	// .aisle("XXX", "C#C", "XMX")
	// .aisle("XSX", "CCC", "XXX")
	// .where('S', controller(blocks(definition.get())))
	// .where('X', blocks(CASING_INVAR_HEATPROOF.get()) .or(autoAbilities(definition.getRecipeTypes())) .or(autoAbilities(true, false, false)))
	// .where('M', abilities(PartAbility.MUFFLER))
	// .where('C', heatingCoils())
	// .where('#', air())
	public static readonly string[] MultiSmelter =
	{
		"XMX",
		"CCC",
		"XSX",
	};

	// =========================================================================
	//  MultiblockTank   (wooden_multiblock_tank / bronze_multiblock_tank /
	//                    steel_multiblock_tank)
	// =========================================================================
	//  Upstream source : GTMultiMachines.java (registerMultiblockTank)
	//  Recipe type(s)  : DUMMY_RECIPES (storage-only)
	//  Upstream pattern:
	// .aisle("CCC", "CCC", "CCC")
	// .aisle("CCC", "C#C", "CCC")
	// .aisle("CCC", "CSC", "CCC")
	// .where('S', controller(blocks(definition.get())))
	// .where('C', blocks(casing.get()) .or(blocks(valve.get()).setMaxGlobalLimited(2, 0)))
	// .where('#', air())
	// Collapsed to a 3x3 front face - the inner air cell (the cavity holding
	// fluid in 3D) has no 2D representation; storage capacity is data on the
	// definition. Char legend below:
	//   S - controller
	//   X - wall casing OR tank valve (max 2 valves, per upstream
	//       `setMaxGlobalLimited(2, 0)`)
	public static readonly string[] MultiblockTank =
	{
		"XXX",
		"XSX",
		"XXX",
	};

	// =========================================================================
	//  NetworkSwitch   (network_switch)
	// =========================================================================
	//  Upstream source : GTResearchMachines.java
	//  Recipe type(s)  : DUMMY_RECIPES
	//  Upstream pattern:
	// .aisle("XXX", "XXX", "XXX")
	// .aisle("XXX", "XAX", "XXX")
	// .aisle("XXX", "XSX", "XXX")
	// .where('S', controller(blocks(definition.getBlock())))
	// .where('A', blocks(ADVANCED_COMPUTER_CASING.get()))
	// .where('X', blocks(COMPUTER_CASING.get()) .or(abilities(PartAbility.INPUT_ENERGY).setMaxGlobalLimited(2, 1)) .or(abilities(PartAbility.COMPUTATION_DATA_TRANSMISSION)) .or(abilities(PartAbility.COMPUTATION_DATA_RECEPTION)) .or(autoAbilities(true, false, false)))
	public static readonly string[] NetworkSwitch =
	{
		"XXX",
		"XSX",
		"XXX",
	};

	// =========================================================================
	//  PowerSubstation   (power_substation)
	// =========================================================================
	//  Upstream source : GTMultiMachines.java
	//  Recipe type(s)  : DUMMY_RECIPES
	//  PORT NOTE:
	//    Terraria-port plan: only one battery-rack layer (the single 9x3 footprint
	//    //    upstream emits at min size). The setRepeatable(...) middle aisle is
	//    //    intentionally NOT supported - we use one fixed shape, not a variable
	//    //    rack stack.
	//  Upstream pattern:
	// .aisle("XXSXX", "XXXXX", "XXXXX", "XXXXX", "XXXXX")
	// .aisle("XXXXX", "XCCCX", "XCCCX", "XCCCX", "XXXXX")
	// .aisle("GGGGG", "GBBBG", "GBBBG", "GBBBG", "GGGGG") .setRepeatable(1, PowerSubstationMachine.MAX_BATTERY_LAYERS)
	// .aisle("GGGGG", "GGGGG", "GGGGG", "GGGGG", "GGGGG")
	// .where('S', controller(blocks(definition.getBlock())))
	// .where('C', blocks(CASING_PALLADIUM_SUBSTATION.get()))
	// .where('X', blocks(CASING_PALLADIUM_SUBSTATION.get()) .or(autoAbilities(true, false, false)) .or(abilities(PartAbility.INPUT_ENERGY, PartAbility.SUBSTATION_INPUT_ENERGY, PartAbility.INPUT_LASER)) .or(abilities(PartAbility.OUTPUT_ENERGY, PartAbility.SUBSTATION_OUTPUT_ENERGY, PartAbility.OUTPUT_LASER)))
	// .where('G', blocks(CASING_LAMINATED_GLASS.get()))
	// .where('B', Predicates.powerSubstationBatteries())
	public static readonly string[] PowerSubstation =
	{
		"GGGGG",
		"GBBBG",
		"XCCCX",
		"XXSXX",
	};

	// =========================================================================
	//  PrimitiveBlastFurnace   (primitive_blast_furnace)
	// =========================================================================
	//  Upstream source : GTMultiMachines.java
	//  Recipe type(s)  : PRIMITIVE_BLAST_FURNACE_RECIPES
	//  Upstream pattern:
	// .aisle("XXX", "XXX", "XXX", "XXX")
	// .aisle("XXX", "X&X", "X#X", "X#X")
	// .aisle("XXX", "XYX", "XXX", "XXX")
	// .where('X', blocks(CASING_PRIMITIVE_BRICKS.get()))
	// .where('#', Predicates.air())
	// .where('&', Predicates.air() .or(Predicates.custom(bws -> GTUtil.isBlockSnow(bws.getBlockState()), null)))
	// .where('Y', Predicates.controller(blocks(definition.getBlock())))
	public static readonly string[] PrimitiveBlastFurnace =
	{
		"XXX",
		"XXX",
		"XYX",
		"XXX",
	};

	// =========================================================================
	//  PrimitivePump   (primitive_pump)
	// =========================================================================
	//  Upstream source : GTMultiMachines.java
	//  Recipe type(s)  : ASSEMBLY_LINE_RECIPES
	//  Upstream pattern:
	// .aisle("XXXX", "##F#", "##F#")
	// .aisle("XXHX", "F##F", "FFFF")
	// .aisle("SXXX", "##F#", "##F#")
	// .where('S', Predicates.controller(blocks(definition.getBlock())))
	// .where('X', blocks(CASING_PUMP_DECK.get()))
	// .where('F', Predicates.frames(GTMaterials.TreatedWood))
	// .where('H', Predicates.abilities(PartAbility.PUMP_FLUID_HATCH) .or(blocks(FLUID_EXPORT_HATCH[ULV].get(), FLUID_EXPORT_HATCH[LV].get())))
	// .where('#', Predicates.any())
	public static readonly string[] PrimitivePump =
	{
		"FFFF",
		"F##F",
		"SXHX",
	};

	// =========================================================================
	//  PyrolyseOven   (pyrolyse_oven)
	// =========================================================================
	//  Upstream source : GTMultiMachines.java
	//  Recipe type(s)  : PYROLYSE_RECIPES
	//  Upstream pattern:
	// .aisle("XXX", "XXX", "XXX")
	// .aisle("CCC", "C#C", "CCC")
	// .aisle("CCC", "C#C", "CCC")
	// .aisle("XXX", "XSX", "XXX")
	// .where('S', Predicates.controller(blocks(definition.get())))
	// .where('X', blocks(MACHINE_CASING_ULV.get()) .or(Predicates.autoAbilities(definition.getRecipeTypes())) .or(Predicates.autoAbilities(true, true, false)))
	// .where('C', Predicates.heatingCoils())
	// .where('#', Predicates.air())
	public static readonly string[] PyrolyseOven =
	{
		"XCCCX",
		"XCCCX",
		"XCCCS",
		"XCCCX",
		"XCCCX",
	};

	// =========================================================================
	//  ResearchStation   (research_station)
	// =========================================================================
	//  Upstream source : GTResearchMachines.java
	//  Recipe type(s)  : RESEARCH_STATION_RECIPES
	//  Upstream pattern:
	// .aisle("XXX", "VVV", "PPP", "PPP", "PPP", "VVV", "XXX")
	// .aisle("XXX", "VAV", "AAA", "AAA", "AAA", "VAV", "XXX")
	// .aisle("XXX", "VAV", "XAX", "XSX", "XAX", "VAV", "XXX")
	// .aisle("XXX", "XAX", "---", "---", "---", "XAX", "XXX")
	// .aisle(" X ", "XAX", "---", "---", "---", "XAX", " X ")
	// .aisle(" X ", "XAX", "-A-", "-H-", "-A-", "XAX", " X ")
	// .aisle(" ", "XXX", "---", "---", "---", "XXX", " ")
	// .where('S', controller(blocks(definition.getBlock())))
	// .where('X', blocks(COMPUTER_CASING.get()))
	// .where(' ', any())
	// .where('-', air())
	// .where('V', blocks(COMPUTER_HEAT_VENT.get()))
	// .where('A', blocks(ADVANCED_COMPUTER_CASING.get()))
	// .where('P', blocks(COMPUTER_CASING.get()) .or(abilities(PartAbility.INPUT_ENERGY).setMaxGlobalLimited(2, 1)) .or(abilities(PartAbility.COMPUTATION_DATA_RECEPTION).setExactLimit(1)) .or(autoAbilities(true, false, false)))
	// .where('H', abilities(PartAbility.OBJECT_HOLDER))
	public static readonly string[] ResearchStation =
	{
		"PPP######",
		"PVP##XXX#",
		"PVAAAAAX#",
		"PVP##XAX#",
		"PVS###H##",
		"PVP######",
		"PVPPPPPP#",
		"PPPPPPPPP",
	};

	// =========================================================================
	//  SteamGrinder   (steam_grinder)
	// =========================================================================
	//  Upstream source : GTMultiMachines.java
	//  Recipe type(s)  : MACERATOR_RECIPES
	//  Upstream pattern:
	// .aisle("XXX", "XXX", "XXX")
	// .aisle("XXX", "X#X", "XXX")
	// .aisle("XXX", "XSX", "XXX")
	// .where('S', Predicates.controller(blocks(definition.getBlock())))
	// .where('#', Predicates.air())
	// .where('X', blocks(CASING_BRONZE_BRICKS.get()) .or(Predicates.abilities(PartAbility.STEAM_IMPORT_ITEMS).setPreviewCount(1)) .or(Predicates.abilities(PartAbility.STEAM_EXPORT_ITEMS).setPreviewCount(1)) .or(Predicates.abilities(PartAbility.STEAM).setExactLimit(1)))
	public static readonly string[] SteamGrinder =
	{
		"XXX",
		"XSX",
		"XXX",
	};

	// =========================================================================
	//  SteamOven   (steam_oven)
	// =========================================================================
	//  Upstream source : GTMultiMachines.java
	//  Recipe type(s)  : FURNACE_RECIPES
	//  Upstream pattern:
	// .aisle("FFF", "XXX", " X ")
	// .aisle("FFF", "X#X", " X ")
	// .aisle("FFF", "XSX", " X ")
	// .where('S', Predicates.controller(blocks(definition.getBlock())))
	// .where('#', Predicates.air())
	// .where(' ', Predicates.any())
	// .where('X', blocks(CASING_BRONZE_BRICKS.get()) .or(Predicates.abilities(PartAbility.STEAM_IMPORT_ITEMS).setPreviewCount(1)) .or(Predicates.abilities(PartAbility.STEAM_EXPORT_ITEMS).setPreviewCount(1)))
	// .where('F', blocks(FIREBOX_BRONZE.get()) .or(Predicates.abilities(PartAbility.STEAM).setExactLimit(1)))
	public static readonly string[] SteamOven =
	{
		"#X#",
		"XSX",
		"XXX",
	};

	// =========================================================================
	//  VacuumFreezer   (vacuum_freezer)
	// =========================================================================
	//  Upstream source : GTMultiMachines.java
	//  Recipe type(s)  : VACUUM_RECIPES
	//  Upstream pattern:
	// .aisle("XXX", "XXX", "XXX")
	// .aisle("XXX", "X#X", "XXX")
	// .aisle("XXX", "XSX", "XXX")
	// .where('S', Predicates.controller(blocks(definition.getBlock())))
	// .where('X', blocks(CASING_ALUMINIUM_FROSTPROOF.get()) .or(Predicates.autoAbilities(definition.getRecipeTypes())) .or(Predicates.autoAbilities(true, false, false)))
	// .where('#', Predicates.air())
	public static readonly string[] VacuumFreezer =
	{
		"XXX",
		"XSX",
		"XXX",
	};

}