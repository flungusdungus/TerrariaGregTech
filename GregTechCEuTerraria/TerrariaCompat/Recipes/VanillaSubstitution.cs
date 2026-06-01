#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Items;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Recipes;

// Single source of truth: Minecraft/Forge ref -> Terraria item / RecipeGroup.
// Centralised so policy is auditable; every new entry = one row.
//
// Four resolution channels (used by IngredientResolver):
//   1. ExactItems   id -> item; one-off (stick -> Wood, diamond -> Diamond).
//   2. SuffixRules  id-suffix -> item; whole families (every *_log -> Wood).
//                   Applied AFTER ExactItems so callers can carve exceptions.
//   3. TagItems     tag -> item; representative for machine-slot matching
//                   (separate from Groups, which serve the workbench bridge).
//   4. Groups       tag -> RecipeGroup id; for VanillaCraftingBridge.
//
// Each entry's per-row inline comment is the SSOT for WHY that mapping was
// chosen - read them at the dictionary below, not a duplicate header table.
//
// Conventions: prefer SuffixRules over enumerating species; document the
// reason inline for non-obvious mappings; tag-form refs need TagItems
// (machine-slot match) AND Groups (workbench bridge) entries.
public static class VanillaItemMap
{
	// ---- ExactItems: one-off, non-pattern singletons --------------------------
	private static readonly Dictionary<string, int> ExactItems = new()
	{
		// Stone family
		{ "minecraft:stone",            ItemID.StoneBlock },
		{ "minecraft:cobblestone",      ItemID.StoneBlock },
		{ "minecraft:granite",          ItemID.Granite },
		{ "minecraft:andesite",         ItemID.SlushBlock },
		{ "minecraft:diorite",          ItemID.EbonstoneBlock },
		{ "minecraft:basalt",           ItemID.PearlstoneBlock },     // dark volcanic -> Pearlstone (rock_breaker infinite source)
		{ "minecraft:deepslate",        ItemID.HellstoneBrick },      // deep-layer stone -> Hellstone Brick
		{ "minecraft:blackstone",       ItemID.AncientHellstoneBrick },// nether blackstone -> Ancient Hellstone Brick
		// GT decorative stone BLOCKS that we replace with Terraria's. The matching
		// `forge:marble` / `forge:granite_red` MultiTagItems entries handle the
		// tag-form refs; these handle direct `gtceu:marble` / `gtceu:red_granite`
		// item refs (recipe outputs, plus any other recipe that names them
		// directly). The GT blocks themselves still exist as decorative casings
		// (CasingRegistry) - this only swaps their recipe-graph identity.
		{ "gtceu:marble",               ItemID.Marble },               // raw Marble Block (not "Smooth Marble Block" = ItemID.MarbleBlock)
		{ "gtceu:red_granite",          ItemID.CrimstoneBlock },
		{ "minecraft:cobblestone_slab", ItemID.StoneSlab },
		{ "minecraft:dirt",             ItemID.DirtBlock },
		{ "minecraft:gravel",           ItemID.SiltBlock },
		{ "minecraft:sand",             ItemID.SandBlock },
		{ "minecraft:sandstone",        ItemID.SandstoneBrick },
		{ "minecraft:obsidian",         ItemID.Obsidian },
		{ "minecraft:soul_sand",        ItemID.AshBlock },    // dark nether soil -> Terraria Ash Block
		{ "minecraft:iron_bars",        ItemID.IronFence },   // decorative metal bars -> Terraria Iron Fence

		// Explosives - MC TNT -> Terraria Bomb; GregTech's dynamite -> Terraria
		// Dynamite. (GT industrial_tnt is dropped at extraction, not ported.)
		{ "minecraft:tnt",              ItemID.Bomb },
		{ "gtceu:dynamite",             ItemID.Dynamite },

		// Snow - the full snow_block / snowball map onto Terraria's. (The thin
		// `minecraft:snow` layer is dropped at extraction, not ported.)
		{ "minecraft:snow_block",       ItemID.SnowBlock },
		{ "minecraft:snowball",         ItemID.Snowball },

		// Ingots / ores - MC "ingot" <-> Terraria "bar".
		{ "minecraft:iron_ingot",       ItemID.IronBar },
		{ "minecraft:gold_ingot",       ItemID.GoldBar },
		{ "minecraft:copper_ingot",     ItemID.CopperBar },
		// `minecraft:raw_X` (raw iron / gold / copper) deliberately NOT
		// substituted to `ItemID.*Ore`. The recipe pipeline resolves them
		// through MaterialItemRegistry's `Mod.TryFind("raw_X")` fallback to
		// the existing `gtceu:raw_X` material items, so the macerator's
		// `forge:raw_materials/X` tag -> 2x crushed_X chain consumes the GT
		// raw chunks, not Terraria ore tiles directly. The Terraria-side
		// 1 ore -> 16 raw_ore conversion lives at
		// `VanillaCraftingBridgeSystem.AddVanillaOreToRawOreRecipes` (vanilla
		// workbench recipe). Without this gap, both the conversion AND the
		// macerator step accepted Terraria ore tiles, collapsing the
		// intended `1 ore -> 16 raw -> 32 crushed` chain to `1 ore -> 2 crushed`.
		{ "minecraft:diamond",          ItemID.Diamond },
		{ "minecraft:emerald",          ItemID.Emerald },

		// Common drops
		// (minecraft:coal -> gtceu:coal_gem via MaterialSubs - ItemID.Coal is the
		// Christmas-stocking gag item with maxStack=1, not a fuel.)
		{ "minecraft:torch",            ItemID.Torch },
		{ "minecraft:glass_pane",       ItemID.GlassWall },
		{ "minecraft:bone",             ItemID.Bone },
		{ "minecraft:anvil",            ItemID.IronAnvil },   // crafting-station anvil -> Terraria Iron Anvil
		{ "minecraft:book",             ItemID.Book },
		{ "minecraft:bookshelf",        ItemID.Bookcase },
		{ "minecraft:lantern",          ItemID.ChainLantern },
		{ "minecraft:pumpkin",          ItemID.Pumpkin },
		{ "minecraft:pumpkin_seeds",    ItemID.PumpkinSeed },
		{ "minecraft:jack_o_lantern",   ItemID.JackOLantern },
		{ "minecraft:slime_ball",       ItemID.Gel },
		{ "minecraft:gunpowder",        ItemID.ExplosivePowder },
		{ "minecraft:string",           ItemID.Silk },
		{ "minecraft:feather",          ItemID.Feather },
		{ "minecraft:leather",          ItemID.Leather },

		// Utility items / blocks
		{ "minecraft:bucket",           ItemID.EmptyBucket },
		{ "minecraft:cauldron",         ItemID.Cauldron },
		{ "minecraft:clock",            ItemID.GoldWatch },
		{ "minecraft:compass",          ItemID.Compass },
		{ "minecraft:glass_bottle",     ItemID.Bottle },
		{ "minecraft:bow",              ItemID.WoodenBow },
		{ "minecraft:chest",            ItemID.Chest },
		{ "minecraft:loom",             ItemID.Loom },
		{ "minecraft:crafting_table",   ItemID.WorkBench },
		{ "minecraft:white_wool",       ItemID.Cloud },       // white wool -> Cloud (dyed wool dropped at extraction)
		{ "minecraft:dropper",          ItemID.DartTrap },
		{ "minecraft:item_frame",       ItemID.ItemFrame },
		{ "gtceu:nano_saber",           ItemID.Muramasa },     // GT energy sword -> Terraria Muramasa
		{ "minecraft:painting",         ItemID.PlacePainting },  // MC painting -> Terraria's "r/Terraria" community painting
		{ "minecraft:redstone_torch",   ItemID.Wire },           // redstone signal source -> Terraria Wire
		{ "minecraft:lever",            ItemID.Lever },
		{ "minecraft:piston",           ItemID.Grate },             // piston -> Grate (mechanical-looking grille; closer aesthetic match than Active Stone Block)
		{ "minecraft:flower_pot",       ItemID.ClayPot },
		{ "minecraft:furnace",          ItemID.Furnace },
		{ "minecraft:blast_furnace",    ItemID.Hellforge },
		{ "minecraft:chain",            ItemID.Chain },
		// `gtceu:iron_ore / copper_ore / gold_ore` (the GregTech stone-host ore
		// BLOCK items) are deliberately NOT substituted to `ItemID.*Ore`. They
		// resolve via the RawOre prefix's `%s_ore` IdPattern alias
		// (MaterialPrefix.cs:41) -> `gtceu:raw_X`, matching the
		// `forge:raw_materials/X` tag path. Without this, the
		// `macerate_X_ore_to_crushed_ore` family (the stone-host variant, sibling
		// of `macerate_raw_X_ore_to_crushed_ore`) substituted to ItemID.*Ore and
		// short-circuited the `1 vanilla ore -> 16 raw -> 32 crushed` chain to
		// `1 vanilla ore -> 2 crushed`. After this change both recipes consume
		// `raw_X`; the stone-host variant additionally drops `stone_dust` as a
		// byproduct, which is the legitimate semantic difference. Stays in
		// `_byUpstreamId` via the IdPattern alias - does NOT need an entry here.

		// GregTech jetpacks -> Terraria flight items (ascending tiers).
		{ "gtceu:liquid_fuel_jetpack",     ItemID.CreativeWings },  // Fledgling Wings
		{ "gtceu:electric_jetpack",        ItemID.FairyWings },
		{ "gtceu:advanced_electric_jetpack", ItemID.Jetpack },

		// Food - apple onto Apple; the enchanted golden apple onto Terraria's
		// closest "special apple", the Candy Apple. (plain golden_apple has no
		// analogue - its recipes are dropped at extraction.)
		{ "minecraft:apple",                  ItemID.Apple },
		{ "minecraft:cactus",                 ItemID.Cactus },
		{ "minecraft:sugar_cane",             ItemID.BambooBlock },        // tall water-edge plant -> Terraria's Bamboo Block
		{ "minecraft:red_mushroom",           ItemID.Mushroom },
		{ "minecraft:glow_berries",           ItemID.GlowingMushroom },    // glowing cave plant -> Glowing Mushroom
		{ "minecraft:enchanted_golden_apple", ItemID.CandyApple },
		{ "minecraft:beef",                   ItemID.Worm },        // raw meat -> Terraria bait (Worm)

		// Fishing - rod + the four fish onto their nearest Terraria catches.
		{ "minecraft:fishing_rod",      ItemID.GoldenFishingRod },
		{ "minecraft:cod",              ItemID.AtlanticCod },
		{ "minecraft:pufferfish",       ItemID.BalloonPufferfish },
		{ "minecraft:salmon",           ItemID.Salmon },
		{ "minecraft:tropical_fish",    ItemID.NeonTetra },

		// Rails -> Terraria's minecart tracks. (activator_rail has no analogue -
		// dropped at extraction.)
		{ "minecraft:rail",             ItemID.MinecartTrack },
		{ "minecraft:powered_rail",     ItemID.BoosterTrack },
		{ "minecraft:detector_rail",    ItemID.PressureTrack },
		{ "minecraft:minecart",         ItemID.Minecart },

		// Dyes - MC's 16 colours mapped onto Terraria's basic dyes. magenta ->
		// Violet. Terraria has no true white / gray dye, so the three MC
		// greyscale dyes map onto the three greyscale-family dyes Terraria does
		// have, lightest-to-darkest: white -> BrightSilver, light_gray -> Silver,
		// gray -> BlackAndWhite (a black<->white gradient - reads as mid-grey).
		{ "minecraft:red_dye",          ItemID.RedDye },
		{ "minecraft:orange_dye",       ItemID.OrangeDye },
		{ "minecraft:yellow_dye",       ItemID.YellowDye },
		{ "minecraft:lime_dye",         ItemID.LimeDye },
		{ "minecraft:green_dye",        ItemID.GreenDye },
		{ "minecraft:cyan_dye",         ItemID.CyanDye },
		{ "minecraft:light_blue_dye",   ItemID.SkyBlueDye },
		{ "minecraft:blue_dye",         ItemID.BlueDye },
		{ "minecraft:purple_dye",       ItemID.PurpleDye },
		{ "minecraft:magenta_dye",      ItemID.VioletDye },
		{ "minecraft:pink_dye",         ItemID.PinkDye },
		{ "minecraft:brown_dye",        ItemID.BrownDye },
		{ "minecraft:black_dye",        ItemID.BlackDye },
		{ "minecraft:white_dye",        ItemID.BrightSilverDye },
		{ "minecraft:light_gray_dye",   ItemID.SilverDye },
		{ "minecraft:gray_dye",         ItemID.BlackAndWhiteDye },
		// GregTech's chemical dyes - the SOLE datagen members of every
		// `forge:dyes/<colour>` tag (Forge injects the vanilla `minecraft:*_dye`
		// at runtime, so they never appear in the datagen tag JSON). Mapping the
		// chemical dye to the same Terraria dye makes `#forge:dyes/<colour>`
		// resolve through normal tag-expansion AND fixes direct chemical-dye refs.
		{ "gtceu:chemical_red_dye",        ItemID.RedDye },
		{ "gtceu:chemical_orange_dye",     ItemID.OrangeDye },
		{ "gtceu:chemical_yellow_dye",     ItemID.YellowDye },
		{ "gtceu:chemical_lime_dye",       ItemID.LimeDye },
		{ "gtceu:chemical_green_dye",      ItemID.GreenDye },
		{ "gtceu:chemical_cyan_dye",       ItemID.CyanDye },
		{ "gtceu:chemical_light_blue_dye", ItemID.SkyBlueDye },
		{ "gtceu:chemical_blue_dye",       ItemID.BlueDye },
		{ "gtceu:chemical_purple_dye",     ItemID.PurpleDye },
		{ "gtceu:chemical_magenta_dye",    ItemID.VioletDye },
		{ "gtceu:chemical_pink_dye",       ItemID.PinkDye },
		{ "gtceu:chemical_brown_dye",      ItemID.BrownDye },
		{ "gtceu:chemical_black_dye",      ItemID.BlackDye },
		{ "gtceu:chemical_white_dye",      ItemID.BrightSilverDye },
		{ "gtceu:chemical_light_gray_dye", ItemID.SilverDye },
		{ "gtceu:chemical_gray_dye",       ItemID.BlackAndWhiteDye },

		// Storage blocks with a direct Terraria equivalent - mapping the member
		// item resolves the `forge:storage_blocks/<x>` tag via tag-expansion.
		{ "minecraft:glass",            ItemID.Glass },
		{ "minecraft:clay",             ItemID.ClayBlock },
		{ "minecraft:ice",              ItemID.IceBlock },
		{ "minecraft:honeycomb_block",  ItemID.HoneyBlock },
		{ "minecraft:bone_block",       ItemID.BoneBlock },
		{ "minecraft:bricks",           ItemID.RedBrick },
	};

	// ---- MaterialSubs: id -> (material, prefix) dynamic item ------------------
	// For vanilla refs that map onto OUR material system rather than a fixed
	// ItemID (e.g. minecraft:stick -> wood_rod via GregTech's abstract Rod
	// prefix on the wood material). Resolved at lookup time through
	// MaterialItemRegistry so the dynamic Type allocation is honored.
	private static readonly Dictionary<string, (string Material, string Prefix)> MaterialSubs = new()
	{
		{ "minecraft:stick",            ("wood", "rod") },

		// Vanilla MC items GregTech unifies as a (material, prefix) form but
		// Terraria lacks - materialised as synthetic GT material items by the
		// datagen Material Item-Tag pipeline (see DataGenerators extras list).
		{ "minecraft:gold_nugget",      ("gold", "nugget") },
		{ "minecraft:iron_nugget",      ("iron", "nugget") },
		{ "minecraft:iron_block",       ("iron", "block") },
		{ "minecraft:gold_block",       ("gold", "block") },
		{ "minecraft:copper_block",     ("copper", "block") },
		{ "minecraft:diamond_block",    ("diamond", "block") },
		{ "minecraft:emerald_block",    ("emerald", "block") },
		{ "minecraft:lapis_block",      ("lapis", "block") },
		{ "minecraft:amethyst_block",   ("amethyst", "block") },
		{ "minecraft:coal_block",       ("coal", "block") },
		{ "minecraft:quartz_block",     ("nether_quartz", "block") },
		{ "minecraft:raw_iron_block",   ("iron", "raw_ore_block") },
		{ "minecraft:raw_gold_block",   ("gold", "raw_ore_block") },
		{ "minecraft:raw_copper_block", ("copper", "raw_ore_block") },

		// Dusts / gems / netherite ingot / redstone+glowstone blocks.
		{ "minecraft:glowstone_dust",   ("glowstone", "dust") },
		{ "minecraft:redstone",         ("redstone", "dust") },
		{ "minecraft:sugar",            ("sugar", "dust") },
		{ "minecraft:blaze_powder",     ("blaze", "dust") },
		{ "minecraft:bone_meal",        ("bone", "dust") },
		{ "minecraft:amethyst_shard",   ("amethyst", "gem") },
		{ "minecraft:lapis_lazuli",     ("lapis", "gem") },
		{ "minecraft:quartz",           ("nether_quartz", "gem") },
		{ "minecraft:coal",             ("coal", "gem") },
		{ "minecraft:charcoal",         ("charcoal", "gem") },
		{ "minecraft:flint",            ("flint", "gem") },
		{ "minecraft:ender_pearl",      ("ender_pearl", "gem") },
		{ "minecraft:ender_eye",        ("ender_eye", "gem") },
		{ "minecraft:echo_shard",       ("echo_shard", "gem") },
		{ "minecraft:nether_star",      ("nether_star", "gem") },
		{ "minecraft:clay_ball",        ("clay", "gem") },
		{ "minecraft:netherite_ingot",  ("netherite", "ingot") },
		{ "minecraft:brick",            ("brick", "ingot") },
		{ "minecraft:honeycomb",        ("wax", "ingot") },
		{ "minecraft:blaze_rod",        ("blaze", "rod") },
		{ "minecraft:redstone_block",   ("redstone", "block") },
		{ "minecraft:glowstone",        ("glowstone", "block") },
		// GregTech unifies the paper material's plate form onto vanilla paper -
		// `#forge:plates/paper` dumps to `minecraft:paper`. Route it to the
		// synthetic gtceu:paper_plate GT material item.
		{ "minecraft:paper",            ("paper", "plate") },
	};

	// ---- SuffixRules: pattern fallback ---------------------------------------
	// Applied AFTER ExactItems so a specific id can opt out of the family rule.
	// Order matters only when suffixes overlap (longest-suffix-match wins).
	private static readonly (string Suffix, int Item)[] SuffixRules = new (string, int)[]
	{
		// Wood - every "this is bark/lumber" upstream item -> ItemID.Wood.
		// MC distinguishes oak_log (round) from oak_wood (all-bark cube) from
		// stripped_oak_wood (all-bark, no bark); Terraria has none of that
		// distinction, so they all collapse to Wood.
		("_log",      ItemID.Wood),         // oak_log, stripped_birch_log, ...
		("_wood",     ItemID.Wood),         // oak_wood, stripped_birch_wood, ...
		("_stem",     ItemID.Wood),         // crimson_stem, warped_stem
		("_planks",   ItemID.WoodPlatform), // oak_planks, dark_oak_planks, ...
		("_sapling",  ItemID.Acorn),        // oak_sapling, birch_sapling, ... (all -> acorn)
	};

	// ---- TagItems: tag -> representative Terraria item ------------------------
	// Used when a recipe ingredient is `{"tag": "minecraft:logs"}` and the
	// caller needs a single Terraria item to match a machine slot against.
	// For workbench bridging, the SAME tag should map via Groups (below) so
	// the player can use ANY group member at a vanilla crafting station.
	private static readonly Dictionary<string, int> TagItems = new()
	{
		{ "minecraft:logs",                       ItemID.Wood },
		{ "minecraft:logs_that_burn",             ItemID.Wood },
		{ "minecraft:oak_logs",                   ItemID.Wood },        // vanilla MC per-species log tag - not in the gtceu datagen dump
		{ "forge:oak_logs",                       ItemID.Wood },
		{ "minecraft:planks",                     ItemID.WoodPlatform },
		{ "minecraft:saplings",                   ItemID.Acorn },
		{ "minecraft:stone_crafting_materials",   ItemID.StoneBlock },  // cobblestone / blackstone / cobbled_deepslate / ... all collapse to StoneBlock
		{ "forge:wood",                           ItemID.Wood },
		{ "forge:saplings",                       ItemID.Acorn },
		{ "forge:cobblestone",                    ItemID.StoneBlock },  // forge alias used by some recipes
		{ "forge:glass",                          ItemID.Glass },       // Forge's all-glass-blocks tag

		// Coral - MC's coral plants/fans collapse to Terraria's generic Coral
		// item; coral *blocks* collapse to the Coralstone Block. The two parent
		// tags (`#forge:corals`, `#forge:coral_blocks`) are what recipes
		// actually reference; the alive/dead sub-tags are kept for completeness.
		{ "forge:corals",                         ItemID.Coral },
		{ "forge:corals/alive",                   ItemID.Coral },
		{ "forge:corals/dead",                    ItemID.Coral },
		{ "forge:coral_fans/alive",               ItemID.Coral },
		{ "forge:coral_fans/dead",                ItemID.Coral },
		{ "forge:coral_plants/alive",             ItemID.Coral },
		{ "forge:coral_plants/dead",              ItemID.Coral },
		{ "forge:coral_blocks",                   ItemID.CoralstoneBlock },
		{ "forge:coral_blocks/alive",             ItemID.CoralstoneBlock },
		{ "forge:coral_blocks/dead",              ItemID.CoralstoneBlock },

		{ "minecraft:coals",                      ItemID.Coal },        // vanilla MC tag (coal + charcoal) - not in the gtceu datagen dump
		{ "forge:chests/wooden",                  ItemID.Chest },       // any wooden chest -> Terraria's generic Chest
		{ "forge:pistons",                        ItemID.Grate },       // piston / sticky_piston -> Grate (mechanical-looking grille; closer aesthetic match)
		{ "minecraft:wool",                       ItemID.Cloud },       // any-colour wool -> Cloud
		{ "forge:glass_panes",                    ItemID.GlassWall },   // plain glass panes -> Terraria Glass Wall

		{ "forge:sand",                           ItemID.SandBlock },   // Forge's all-sand tag
		{ "minecraft:sand",                       ItemID.SandBlock },   // vanilla MC sand tag (sand + red_sand) - not in the gtceu datagen dump
		{ "minecraft:smelts_to_glass",            ItemID.SandBlock },   // vanilla MC tag (sand / red_sand - anything that smelts to glass)
	};

	// ---- MultiTagItems: tag -> multiple representative Terraria items --------
	// EXCLUSIVE substitution - when an entry matches, the resolver SHORT-CIRCUITS
	// the registry-tag-dump expansion (Pass B) so dump-listed gtceu:* members
	// (smooth_marble casing, etc.) DON'T also slip in as recipe inputs. Use this
	// when the substitution set is the complete intended player-facing match
	// surface. `TagItems` stays additive (so forge:wood still admits rubber_log
	// alongside Terraria Wood).
	private static readonly Dictionary<string, int[]> MultiTagItems = new()
	{
		// MC `#minecraft:fishes` - every species of fish. Common catchable
		// Terraria fish so any fish-input recipe accepts what the player has.
		{ "minecraft:fishes", new int[] {
			ItemID.Bass, ItemID.Trout, ItemID.Salmon, ItemID.AtlanticCod,
			ItemID.Tuna, ItemID.RedSnapper, ItemID.NeonTetra,
			ItemID.BalloonPufferfish,
		}},
		// GT marble -> Terraria Marble Block ONLY. NOTE: `ItemID.Marble` (3081)
		// is the raw cave-biome Marble Block we want; `ItemID.MarbleBlock`
		// (3066) is "Smooth Marble Block" (crafted polished variant) - Terraria's
		// naming is inverted. Same trap for Granite (`Granite` raw vs
		// `GraniteBlock` smooth).
		{ "forge:marble",       new int[] { ItemID.Marble } },
		// GT red granite -> Terraria Crimstone Block ONLY (same reason).
		{ "forge:granite_red",  new int[] { ItemID.CrimstoneBlock } },
	};

	// ---- Groups: tag -> RecipeGroup id ----------------------------------------
	// Used ONLY by VanillaCraftingBridge for workbench / furnace recipes.
	// RecipeGroups let Terraria accept any group member in a slot, which is
	// the closest analogue to MC's tag-based ingredient matching.
	private static readonly Dictionary<string, int> Groups = new()
	{
		// Wood tag - Terraria's vanilla "Wood" group covers every wood type.
		{ "minecraft:logs",             RecipeGroupID.Wood },
		{ "minecraft:logs_that_burn",   RecipeGroupID.Wood },
		{ "minecraft:planks",           RecipeGroupID.Wood },
		{ "forge:wood",                 RecipeGroupID.Wood },

		// Iron-bar-equivalent (Iron OR Lead in Terraria).
		{ "forge:ingots/iron",          RecipeGroupID.IronBar },

		// Sand
		{ "minecraft:sand",             RecipeGroupID.Sand },
		{ "forge:sand",                 RecipeGroupID.Sand },
	};

	// === Lookups ================================================================

	public static bool TryGet(string item, out int itemType)
	{
		if (ExactItems.TryGetValue(item, out itemType)) return true;
		if (MaterialSubs.TryGetValue(item, out var mp))
		{
			var t = MaterialItemRegistry.Get(mp.Material, mp.Prefix);
			if (t.HasValue) { itemType = t.Value; return true; }
		}
		// SuffixRules are scoped to `minecraft:` only - otherwise modded items
		// that happen to end in a wood suffix collide. e.g. `gtceu:treated_wood_planks`
		// is a specific GregTech item with its own registration; collapsing it
		// to WoodPlatform via "_planks" produced a recipe-browser duplicate
		// (lathe_planks and treated_wood_sticks both showing WoodPlatform -> rods).
		if (item.StartsWith("minecraft:", System.StringComparison.Ordinal))
		{
			foreach (var (suffix, t) in SuffixRules)
			{
				if (item.EndsWith(suffix)) { itemType = t; return true; }
			}
		}
		itemType = 0;
		return false;
	}

	public static bool TryGetTagItem(string tag, out int itemType) =>
		TagItems.TryGetValue(tag, out itemType);

	public static bool TryGetTagItems(string tag, out int[] itemTypes)
	{
		if (MultiTagItems.TryGetValue(tag, out var arr)) { itemTypes = arr; return true; }
		itemTypes = System.Array.Empty<int>();
		return false;
	}

	// Runtime-registered tag -> RecipeGroup, for groups built from dynamically
	// registered content (e.g. the crafting-tool catalyst groups - see
	// ToolRecipeGroups). Registered in ModSystem.AddRecipeGroups, before the
	// VanillaCraftingBridge runs in AddRecipes.
	private static readonly Dictionary<string, int> _runtimeGroups = new();

	public static void RegisterGroup(string tag, int groupId) => _runtimeGroups[tag] = groupId;

	public static bool TryGetGroup(string tag, out int id) =>
		Groups.TryGetValue(tag, out id) || _runtimeGroups.TryGetValue(tag, out id);
}
