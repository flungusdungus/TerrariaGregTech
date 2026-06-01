#nullable enable
using GregTechCEuTerraria.Api.Data.Chemical.Material;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Fluids.Store;
using GregTechCEuTerraria.Api.Fluids;
using System;
using System.Collections.Generic;

namespace GregTechCEuTerraria.Common.Materials;

// One physical "prefix" form (ingot/dust/plate/rod/...). Mirrors GTCEu's
// TagPrefix concept. Texture rendering is no longer driven from here - material
// items replay the dump's `render.layers` (see RegistryDump / MaterialItem) -
// so this carries only identity (Id), display, the upstream id/tag patterns,
// and the generation predicate (AppliesTo; still consulted for storage blocks).
public sealed record MaterialPrefix(
	string Id,
	string DisplayTemplate,
	Func<Material, bool> AppliesTo,
	// Upstream item-id patterns this prefix satisfies. `%s` is replaced by the
	// material id at registration. Mirrors upstream's TagPrefix.idPattern;
	// multiple entries handle aliases (e.g. RawOre's `raw_%s` AND `%s_ore`).
	string[]? IdPatterns = null,
	// Upstream tag-path patterns. `%s` is the material id. Mirrors
	// upstream's TagPrefix.defaultTagPath.
	string[]? TagPaths = null
);

public static class MaterialPrefixes
{
	private static bool HasForm(Material m, string f) => m.Forms.Contains(f);
	private static bool HasFlag(Material m, string f) => m.Flags.Contains(f);
	private static bool IsSolid(Material m) => HasForm(m, "INGOT") || HasForm(m, "DUST") || HasForm(m, "GEM") || HasForm(m, "WOOD") || HasForm(m, "POLYMER");

	// IdPatterns / TagPaths lifted verbatim from upstream TagPrefix.java.
	// RawOre intentionally collapses both `raw_%s` (the raw_ore prefix) and
	// `%s_ore` (upstream's OreBlockPlace - we don't have a separate ore-block
	// item form, so recipes referencing the ore block item resolve to raw_ore).
	public static readonly MaterialPrefix RawOre      = new("raw_ore",     "Raw {0}",                  m => HasForm(m, "ORE"),
		IdPatterns: new[] {"raw_%s", "%s_ore"},                       TagPaths: new[] {"raw_materials/%s", "ores/%s"});
	public static readonly MaterialPrefix Ingot       = new("ingot",       "{0} Ingot",                m => HasForm(m, "INGOT") || HasForm(m, "POLYMER"),
		IdPatterns: new[] {"%s_ingot"},                               TagPaths: new[] {"ingots/%s"});
	public static readonly MaterialPrefix Nugget      = new("nugget",      "{0} Nugget",               m => HasForm(m, "INGOT"),
		IdPatterns: new[] {"%s_nugget"},                              TagPaths: new[] {"nuggets/%s"});
	public static readonly MaterialPrefix Gem         = new("gem",         "{0}",                      m => HasForm(m, "GEM"),
		IdPatterns: new[] {"%s_gem", "%s"},                           TagPaths: new[] {"gems/%s"});
	public static readonly MaterialPrefix Dust        = new("dust",        "{0} Dust",                 IsSolid,
		IdPatterns: new[] {"%s_dust"},                                TagPaths: new[] {"dusts/%s"});
	public static readonly MaterialPrefix SmallDust   = new("small_dust",  "Small Pile of {0} Dust",   IsSolid,
		IdPatterns: new[] {"small_%s_dust"},                          TagPaths: new[] {"small_dusts/%s"});
	public static readonly MaterialPrefix TinyDust    = new("tiny_dust",   "Tiny Pile of {0} Dust",    IsSolid,
		IdPatterns: new[] {"tiny_%s_dust"},                           TagPaths: new[] {"tiny_dusts/%s"});
	public static readonly MaterialPrefix Plate       = new("plate",       "{0} Plate",                m => HasFlag(m, "GENERATE_PLATE"),
		IdPatterns: new[] {"%s_plate"},                               TagPaths: new[] {"plates/%s"});
	// Upstream TagPrefix.java:445-447 - `hasIngotProperty && GENERATE_PLATE && !NO_SMASHING`.
	// (My earlier `GENERATE_DOUBLE_PLATE` predicate was invented - no such flag
	// exists upstream; double_plate is just "this material can be made into plates
	// and bent into thicker ones".)
	public static readonly MaterialPrefix DoublePlate = new("double_plate","Double {0} Plate",         m => HasForm(m, "INGOT") && HasFlag(m, "GENERATE_PLATE") && !HasFlag(m, "NO_SMASHING"),
		IdPatterns: new[] {"double_%s_plate"},                        TagPaths: new[] {"double_plates/%s"});
	public static readonly MaterialPrefix DensePlate  = new("dense_plate", "Dense {0} Plate",          m => HasFlag(m, "GENERATE_DENSE"),
		IdPatterns: new[] {"dense_%s_plate"},                         TagPaths: new[] {"dense_plates/%s"});
	public static readonly MaterialPrefix Foil        = new("foil",        "{0} Foil",                 m => HasFlag(m, "GENERATE_FOIL"),
		IdPatterns: new[] {"%s_foil"},                                TagPaths: new[] {"foils/%s"});
	public static readonly MaterialPrefix Rod         = new("rod",         "{0} Rod",                  m => HasFlag(m, "GENERATE_ROD"),
		IdPatterns: new[] {"%s_rod"},                                 TagPaths: new[] {"rods/%s"});
	public static readonly MaterialPrefix LongRod     = new("long_rod",    "Long {0} Rod",             m => HasFlag(m, "GENERATE_LONG_ROD"),
		IdPatterns: new[] {"long_%s_rod"},                            TagPaths: new[] {"rods/long/%s"});
	public static readonly MaterialPrefix Bolt        = new("bolt",        "{0} Bolt",                 m => HasFlag(m, "GENERATE_BOLT_SCREW"),
		IdPatterns: new[] {"%s_bolt"},                                TagPaths: new[] {"bolts/%s"});
	public static readonly MaterialPrefix Screw       = new("screw",       "{0} Screw",                m => HasFlag(m, "GENERATE_BOLT_SCREW"),
		IdPatterns: new[] {"%s_screw"},                               TagPaths: new[] {"screws/%s"});
	public static readonly MaterialPrefix Ring        = new("ring",        "{0} Ring",                 m => HasFlag(m, "GENERATE_RING"),
		IdPatterns: new[] {"%s_ring"},                                TagPaths: new[] {"rings/%s"});
	public static readonly MaterialPrefix Round       = new("round",       "{0} Round",                m => HasFlag(m, "GENERATE_ROUND"),
		IdPatterns: new[] {"%s_round"},                               TagPaths: new[] {"rounds/%s"});
	public static readonly MaterialPrefix Gear        = new("gear",        "{0} Gear",                 m => HasFlag(m, "GENERATE_GEAR"),
		IdPatterns: new[] {"%s_gear"},                                TagPaths: new[] {"gears/%s"});
	public static readonly MaterialPrefix SmallGear   = new("small_gear",  "Small {0} Gear",           m => HasFlag(m, "GENERATE_SMALL_GEAR"),
		IdPatterns: new[] {"small_%s_gear"},                          TagPaths: new[] {"small_gears/%s"});
	public static readonly MaterialPrefix Spring      = new("spring",      "{0} Spring",               m => HasFlag(m, "GENERATE_SPRING"),
		IdPatterns: new[] {"%s_spring"},                              TagPaths: new[] {"springs/%s"});
	public static readonly MaterialPrefix SmallSpring = new("small_spring","Small {0} Spring",         m => HasFlag(m, "GENERATE_SPRING_SMALL"),
		IdPatterns: new[] {"small_%s_spring"},                        TagPaths: new[] {"small_springs/%s"});
	public static readonly MaterialPrefix Rotor       = new("rotor",       "{0} Rotor",                m => HasFlag(m, "GENERATE_ROTOR"),
		IdPatterns: new[] {"%s_rotor"},                               TagPaths: new[] {"rotors/%s"});
	public static readonly MaterialPrefix FineWire    = new("fine_wire",   "Fine {0} Wire",            m => HasFlag(m, "GENERATE_FINE_WIRE"),
		IdPatterns: new[] {"fine_%s_wire"},                           TagPaths: new[] {"fine_wires/%s"});

	// === Crushed ore chain (macerator -> ore_washer -> thermal_centrifuge) ===
	// Predicate is conservative: any material with ORE form participates in this
	// chain. Upstream's exact gating uses additional flags we don't extract yet;
	// false positives just register an unused item.
	//
	// crushed_purified specifically shares `crushed`'s primary+secondary textures
	// (its model JSON is layer0=crushed, layer1=crushed_secondary, no overlay)
	// - the standalone `crushed_purified.png` file in upstream's tree is unused
	// by the model and we deliberately don't reference it.
	public static readonly MaterialPrefix Crushed            = new("crushed",            "Crushed {0} Ore",             m => HasForm(m, "ORE"),
		IdPatterns: new[] {"crushed_%s_ore"},                         TagPaths: new[] {"crushed_ores/%s"});
	public static readonly MaterialPrefix CrushedPurified    = new("crushed_purified",   "Purified Crushed {0} Ore",    m => HasForm(m, "ORE"),
		IdPatterns: new[] {"purified_%s_ore"},                        TagPaths: new[] {"purified_ores/%s"});
	public static readonly MaterialPrefix CrushedRefined     = new("crushed_refined",    "Refined {0} Ore",             m => HasForm(m, "ORE"),
		IdPatterns: new[] {"refined_%s_ore"},                         TagPaths: new[] {"refined_ores/%s"});
	public static readonly MaterialPrefix PureDust           = new("pure_dust",          "Purified Pile of {0} Dust",   m => HasForm(m, "ORE"),
		IdPatterns: new[] {"pure_%s_dust"},                           TagPaths: new[] {"pure_dusts/%s"});
	public static readonly MaterialPrefix ImpureDust         = new("impure_dust",        "Impure Pile of {0} Dust",     m => HasForm(m, "ORE"),
		IdPatterns: new[] {"impure_%s_dust"},                         TagPaths: new[] {"impure_dusts/%s"});

	// === Gem quality grades ===
	public static readonly MaterialPrefix ChippedGem    = new("chipped_gem",  "Chipped {0}",   m => HasForm(m, "GEM"),
		IdPatterns: new[] {"chipped_%s_gem"},                         TagPaths: new[] {"chipped_gems/%s"});
	public static readonly MaterialPrefix FlawedGem     = new("flawed_gem",   "Flawed {0}",    m => HasForm(m, "GEM"),
		IdPatterns: new[] {"flawed_%s_gem"},                          TagPaths: new[] {"flawed_gems/%s"});
	public static readonly MaterialPrefix FlawlessGem   = new("flawless_gem", "Flawless {0}",  m => HasForm(m, "GEM"),
		IdPatterns: new[] {"flawless_%s_gem"},                        TagPaths: new[] {"flawless_gems/%s"});
	public static readonly MaterialPrefix ExquisiteGem  = new("exquisite_gem","Exquisite {0}", m => HasForm(m, "GEM"),
		IdPatterns: new[] {"exquisite_%s_gem"},                       TagPaths: new[] {"exquisite_gems/%s"});

	// === Ingot intermediates ===
	// HotIngot is the EBF intermediate - only emitted for blast-smelted materials,
	// detected via BlastTemperatureK presence.
	public static readonly MaterialPrefix HotIngot      = new("hot_ingot",    "Hot {0} Ingot",    m => HasForm(m, "INGOT") && m.BlastTemperatureK.HasValue,
		IdPatterns: new[] {"hot_%s_ingot"},                           TagPaths: new[] {"hot_ingots/%s"});
	public static readonly MaterialPrefix DoubleIngot   = new("double_ingot", "Double {0} Ingot", m => HasForm(m, "INGOT"),
		IdPatterns: new[] {"double_%s_ingot"},                        TagPaths: new[] {"double_ingots/%s"});

	// === Wire variants (wiremill / wire-combining shapeless) ===
	// Upstream gates these on GENERATE_WIRE, which we don't extract yet - use
	// GENERATE_FINE_WIRE as a proxy. Upstream itself uses MaterialIconType.wire
	// for every stack size and renders them as cable BLOCKS in inventory (bundle
	// count is part of the 3D model), so there's no flat-icon variant per stack
	// size to sync.
	//
	// We ship generated bar textures of progressively-thicker rectangles per
	// tier (in Content/Items/Materials/dull/wire*.png) - single = thinnest,
	// hex = thickest - tinted by material color. Clean and instantly
	// distinguishable; the strand-stacking visual was tried and looked bad.
	// Upstream id pattern is the default TagPrefix `%s_<lowerCaseName>` -
	// `%s_single_wire`, ... (verified against the registry dump). Material name
	// leads; the size word is part of the suffix.
	public static readonly MaterialPrefix SingleWire    = new("single_wire",     "{0} Wire",            m => HasFlag(m, "GENERATE_FINE_WIRE"),
		IdPatterns: new[] {"%s_single_wire"});
	public static readonly MaterialPrefix DoubleWire    = new("double_wire",     "Double {0} Wire",     m => HasFlag(m, "GENERATE_FINE_WIRE"),
		IdPatterns: new[] {"%s_double_wire"});
	public static readonly MaterialPrefix QuadrupleWire = new("quadruple_wire",  "Quadruple {0} Wire",  m => HasFlag(m, "GENERATE_FINE_WIRE"),
		IdPatterns: new[] {"%s_quadruple_wire"});
	public static readonly MaterialPrefix OctalWire     = new("octal_wire",      "Octal {0} Wire",      m => HasFlag(m, "GENERATE_FINE_WIRE"),
		IdPatterns: new[] {"%s_octal_wire"});
	public static readonly MaterialPrefix HexWire       = new("hex_wire",        "Hex {0} Wire",        m => HasFlag(m, "GENERATE_FINE_WIRE"),
		IdPatterns: new[] {"%s_hex_wire"});

	// === Misc machine-recipe outputs ===
	public static readonly MaterialPrefix Lens          = new("lens",          "{0} Lens",          m => HasFlag(m, "GENERATE_LENS"),
		IdPatterns: new[] {"%s_lens"},                                TagPaths: new[] {"lenses/%s"});
	public static readonly MaterialPrefix TurbineBlade  = new("turbine_blade", "{0} Turbine Blade", m => HasForm(m, "INGOT"),
		IdPatterns: new[] {"%s_turbine_blade"});

	// === Tool heads (inert items) ===
	//
	// Upstream registers tool heads conditional on the material having a
	// matching TOOL property (BUZZSAW, DRILL, ...). We don't track tool
	// properties in our Material JSON (they sit in the `unported` list), so
	// we over-register: any material with GENERATE_PLATE gets the prefix.
	// The extra entries are inert items that sit in inventory - harmless.
	// When we wire a real tool system, tighten these predicates to mirror
	// upstream's hasFlag(GENERATE_PLATE) && hasToolType(...) chain.
	public static readonly MaterialPrefix BuzzSawBlade   = new("buzz_saw_blade",   "{0} Buzzsaw Blade",   m => HasFlag(m, "GENERATE_PLATE"),
		IdPatterns: new[] {"%s_buzz_saw_blade"});
	public static readonly MaterialPrefix ChainsawHead   = new("chainsaw_head",    "{0} Chainsaw Head",   m => HasFlag(m, "GENERATE_PLATE"),
		IdPatterns: new[] {"%s_chainsaw_head"});
	public static readonly MaterialPrefix DrillHead      = new("drill_head",       "{0} Drill Head",      m => HasFlag(m, "GENERATE_PLATE"),
		IdPatterns: new[] {"%s_drill_head"});
	public static readonly MaterialPrefix ScrewdriverTip = new("screwdriver_tip",  "{0} Screwdriver Tip", m => HasFlag(m, "GENERATE_PLATE"),
		IdPatterns: new[] {"%s_screwdriver_tip"});
	public static readonly MaterialPrefix WireCutterHead = new("wire_cutter_head", "{0} Wire Cutter Head", m => HasFlag(m, "GENERATE_PLATE"),
		IdPatterns: new[] {"%s_wire_cutter_head"});
	public static readonly MaterialPrefix WrenchTip      = new("wrench_tip",       "{0} Wrench Tip",      m => HasFlag(m, "GENERATE_PLATE"),
		IdPatterns: new[] {"%s_wrench_tip"});

	// === Wood-form prefixes ===
	//
	// Planks - flat-panel form of a WOOD-form material (only `wood` and
	// `treated_wood` qualify in our material set). Upstream ships per-material
	// `block/<material>_planks.png` art; we don't sync those individually, so
	// for now we reuse the plate sprite (visually a flat-panel form too).
	// Same gameplay role: inert intermediate consumed by lathe -> rod and
	// produced by cutter from logs/wood.
	public static readonly MaterialPrefix Planks        = new("planks",        "{0} Planks",        m => HasForm(m, "WOOD"),
		IdPatterns: new[] {"%s_planks"},                              TagPaths: new[] {"planks/%s"});

	// === Storage block (9 ingots / 9 gems compressed into a placeable block) ===
	public static readonly MaterialPrefix Block         = new("block",         "Block of {0}",      m => HasForm(m, "INGOT") || HasForm(m, "GEM"),
		IdPatterns: new[] {"%s_block"},                               TagPaths: new[] {"storage_blocks/%s"});

	// === Raw-ore block (9 raw ore compressed) - upstream TagPrefix.rawOreBlock.
	// Id raw_ore_block -> SnakeToCamel "rawOreBlock", matching the dump's prefix.
	public static readonly MaterialPrefix RawOreBlock   = new("raw_ore_block", "Block of Raw {0}",  m => HasForm(m, "ORE"),
		IdPatterns: new[] {"raw_%s_block"},                           TagPaths: new[] {"storage_blocks/raw_%s"});

	// === Frame (material scaffold block) - upstream TagPrefix.frameGt. A
	// walk-through structural block; the recipe tag is `forge:frames/<material>`.
	public static readonly MaterialPrefix Frame         = new("frame",         "{0} Frame",         m => HasFlag(m, "GENERATE_FRAME"),
		IdPatterns: new[] {"%s_frame"},                               TagPaths: new[] {"frames/%s"});

	public static readonly IReadOnlyList<MaterialPrefix> All = new[]
	{
		RawOre,
		Ingot, Nugget, Gem, Dust, SmallDust, TinyDust,
		Plate, DoublePlate, DensePlate, Foil,
		Rod, LongRod, Bolt, Screw, Ring, Round,
		Gear, SmallGear, Spring, SmallSpring, Rotor, FineWire,
		// New (porting wave for crushed ore chain + gem qualities + wires + misc):
		Crushed, CrushedPurified, CrushedRefined, PureDust, ImpureDust,
		ChippedGem, FlawedGem, FlawlessGem, ExquisiteGem,
		HotIngot, DoubleIngot,
		SingleWire, DoubleWire, QuadrupleWire, OctalWire, HexWire,
		Lens, TurbineBlade,
		BuzzSawBlade, ChainsawHead, DrillHead, ScrewdriverTip, WireCutterHead, WrenchTip,
		Planks,
		Block, RawOreBlock, Frame,
	};
}
