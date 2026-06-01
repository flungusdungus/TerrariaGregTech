#!/usr/bin/env python3
"""
Snapshot upstream's per-recipe runData JSON output into a single bundled
file for our recipe pipeline.

Workflow:
  1. In `GregTech-Modern-1.20.1/`, run `./gradlew runData`. This produces
     ~33k JSON files under `runtime/data/gtceu/recipes/<type>/<id>.json`
     (and various sub-paths) using upstream's GTRecipeSerializer schema.
  2. Run this script to walk that tree, derive `<type>/<id>` from each
     file's relative path, inject it as the recipe's `id` field, and emit
     one flat JSON array.
  3. Loader (`Common/Recipes/RecipeJsonLoader.cs`) reads the bundle, calls
     `GTRecipeSerializer.Read` on each entry.

Why bundled:
  - 33k small files is a checkout/loadtime headache; a single sequential-
    read JSON is faster.
  - One file ships easier in the mod distribution.
  - Diff-friendly (one big file shows recipe drift across upstream versions).

Default invocation:
  python tools/scripts/snapshot-recipes.py

Custom paths:
  python tools/scripts/snapshot-recipes.py \\
      --input  GregTech-Modern-1.20.1/runtime/data/gtceu/recipes \\
      --output GregTechCEuTerraria/Data/Recipes/all.json \\
      --clean
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from collections import Counter
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_INPUT  = REPO_ROOT / "GregTech-Modern-1.20.1" / "src" / "generated" / "resources" / "data" / "gtceu" / "recipes"
DEFAULT_OUTPUT = REPO_ROOT / "GregTechCEuTerraria" / "Data" / "Recipes" / "all.json"


def walk_recipes(input_dir: Path):
    """Yield (recipe_id, json_obj) for every .json file under input_dir.

    recipe_id is the path relative to input_dir, minus the .json extension,
    with backslashes normalized to forward slashes (Windows compatibility).
    Examples:
      runtime/data/gtceu/recipes/macerator/iron_dust.json  -> id="macerator/iron_dust"
      .../assembler/circuits/lv_circuit.json               -> id="assembler/circuits/lv_circuit"
    """
    if not input_dir.is_dir():
        raise SystemExit(f"Input directory not found: {input_dir}\n"
                         "Did you run `./gradlew runData` in the upstream tree?")

    for json_path in sorted(input_dir.rglob("*.json")):
        rel = json_path.relative_to(input_dir).with_suffix("")
        recipe_id = str(rel).replace(os.sep, "/")
        try:
            with json_path.open(encoding="utf-8") as f:
                obj = json.load(f)
        except json.JSONDecodeError as e:
            print(f"  WARN: skipping malformed JSON {json_path}: {e}", file=sys.stderr)
            continue
        yield recipe_id, obj


# Non-oak wood species. Minecraft has one recipe per wood species (birch
# planks, acacia stairs, jungle boat, ...); in our port every wood collapses to
# Terraria's generic Wood, so the per-species variants are pure duplicates.
# We keep ONLY the oak variant - it stands in for the whole "any wood" family
# (resolved through the wood RecipeGroup, like Terraria's any-wood campfire).
NON_OAK_WOOD = (
    "spruce", "birch", "jungle", "acacia", "dark_oak",
    "mangrove", "cherry", "bamboo", "crimson", "warped",
)


def is_non_oak_wood_recipe(recipe_id: str) -> bool:
    """True if the recipe is named for a non-oak wood species.

    Checks the recipe id ONLY. GTCEu/MC name a recipe after its primary
    output, so every per-species variant carries the species token in its id
    (`cutter/birch_planks`, `macerator/macerate_dark_oak_boat`, ...) while the
    oak variant does not. Deliberately NOT a deep scan of the recipe body - a
    recursive scan would also catch recipes that merely have a `jungle` biome
    condition or a crimson/warped byproduct and drop genuinely useful
    non-wood recipes."""
    rid = recipe_id.lower()
    return any(sp in rid for sp in NON_OAK_WOOD)


def clean_legacy(legacy_dir: Path) -> int:
    """Delete legacy per-station JSON files (our old normalized format)."""
    if not legacy_dir.is_dir():
        return 0
    removed = 0
    for f in legacy_dir.glob("*.json"):
        # Skip the new bundle if it lives in the same directory.
        if f.name == "all.json":
            continue
        f.unlink()
        removed += 1
    return removed


# Materials / items deliberately not ported (no Terraria analogue / gameplay
# role). Any recipe mentioning one - in its id OR anywhere in its body - is
# dropped wholesale (see mentions_removed). The token strings below are
# self-describing; only the non-obvious bits are worth a comment:
#
#  - Tokens are SUBSTRING-matched, so one token catches a whole family:
#    "mud" -> mud/packed_mud/mud_brick*; "moss" -> moss_block/mossy_*; likewise
#    wheat, candle, azalea, terracotta, *_coral, *_minecart, *_wool, etc. A
#    trailing "_" (e.g. "smooth_") scopes the match.
#  - Keep each token specific enough not to hit an id that must SURVIVE:
#      * white_wool is kept (-> Cloud) - the 15 dyed wools are listed one by one.
#      * plain anvil / tnt / minecart are kept - the tokens target only the
#        variants (chipped_/damaged_anvil, industrial_tnt, *_minecart).
#      * polished_<stone> lists each MC stone individually so it cannot catch
#        gtceu's polished_marble / polished_red_granite (kept as casings).
#      * treated_wood_* drops only the shaped furniture - treated_wood_planks
#        and its dust/plate/rod/frame/pipe forms are kept.
#      * the `smoking` recipe TYPE is a different string - the "smoker" token
#        does not touch it.
#  - Whole-id cases a substring token would WRONGLY catch live in
#    REMOVED_EXACT_IDS instead, not here.
REMOVED_TOKENS = (
    "sculk", "end_rod", "grass_block", "mycelium", "terracotta", "mud",
    "bubble_coral", "brain_coral", "fire_coral", "horn_coral", "tube_coral",
    "composter", "hopper", "chest_minecart", "furnace_minecart", "tnt_minecart",
    "cartography_table", "industrial_tnt", "chest_boat", "wheat", "fire_charge",
    "saddle", "lectern", "chiseled_bookshelf", "enchanted_book", "writable_book",
    "cake", "wooden_slabs", "iron_door", "iron_trapdoor", "oak_trapdoor", "wooden_trapdoors",
    "trapped_chest", "soul_lantern", "sea_lantern", "soul_torch", "magma_cream",
    "cut_copper", "oxidized_copper", "exposed_copper", "weathered_copper", "waxed",
    "name_tag", "moss", "comparator", "tinted_glass", "tripwire_hook",
    "chipped_anvil", "damaged_anvil", "beacon", "enchanting_table", "banner",
    "orange_wool", "magenta_wool", "light_blue_wool", "yellow_wool", "lime_wool",
    "pink_wool", "gray_wool", "light_gray_wool", "cyan_wool", "purple_wool",
    "blue_wool", "brown_wool", "green_wool", "red_wool", "black_wool",
    "polished_andesite", "polished_blackstone", "polished_deepslate",
    "polished_diorite", "polished_granite",
    "deepslate_tile", "end_stone", "red_sand", "netherrack",
    "conduit", "bowl", "crossbow", "daylight_detector", "dead_bush",
    "dried_kelp_block", "fletching_table", "azalea", "jukebox", "ladder",
    "note_block", "scaffolding", "smithing_table",
    "oak_slab", "oak_stairs", "oak_boat", "oak_door", "oak_fence", "oak_sign",
    "smooth_", "smoker", "bread", "dough",
    "treated_wood_boat", "treated_wood_door", "treated_wood_fence",
    "treated_wood_sign", "treated_wood_slab", "treated_wood_stairs",
    "treated_wood_trapdoor",
    "candle", "white_bed", "white_carpet",
    "end_crystal", "ghast_tear", "polished_basalt", "stained_glass",
    "activator_rail", "nether_brick",
    "hazmat", "flint_and_steel", "horse_armor", "sticky_piston", "iron_door",
    "slime_block", "ender_chest", "rubber_slab", "glass_vial",
    # GregTech rubber decorative blocks/items - no Terraria role (the
    # functional rubber forms - log/wood/planks/ingot/dust/plate/rod - are kept).
    # "rubber_fence" also catches rubber_fence_gate; "hanging_sign" (above)
    # already catches rubber_hanging_sign, so only the plain rubber_sign needs listing.
    "rubber_boat", "rubber_door", "rubber_gloves", "rubber_fence", "rubber_trapdoor",
    "rubber_sign",
    # Ore indicator blocks - GregTech generates one `<material>_indicator` per
    # ore material (a surface marker hinting at a buried vein). No Terraria role;
    # "_indicator" catches every one and matches nothing else.
    "_indicator",
    # Every pressure plate / button - vanilla MC (wood + stone + weighted) and
    # gtceu wood-material variants. All decorative redstone parts, none ported.
    "_pressure_plate", "_button",
    "chainmail", "face_mask",
    # GregTech bronze / steel / titanium armor sets - not ported. Per-piece
    # tokens: a bare `bronze`/`steel`/`titanium` would hit every material item.
    "bronze_helmet", "bronze_chestplate", "bronze_leggings", "bronze_boots",
    "steel_helmet", "steel_chestplate", "steel_leggings", "steel_boots",
    "titanium_helmet", "titanium_chestplate", "titanium_leggings", "titanium_boots",
    # Misc unported MC blocks/redstone. `stone_bricks` also catches the
    # cracked_/chiseled_/mossy_ stone-brick variants.
    "rubber_stairs", "coarse_dirt", "lodestone", "stone_bricks", "cut_sandstone",
    "chiseled_sandstone", "dispenser", "observer", "respawn_anchor", "stonecutter",
    "dripstone_block", "pointed_dripstone",
    # Minecraft flowers / plants / crops / seeds - no Terraria recipe use.
    # `beetroot`/`carrot`/`torchflower` also catch their _seeds/_soup/etc.;
    # `_tulip` catches all four tulip colours.
    "azure_bluet", "blue_orchid", "cornflower", "dandelion", "lilac",
    "lily_of_the_valley", "oxeye_daisy", "peony", "poppy", "rose_bush",
    "wither_rose", "_tulip", "sunflower", "torchflower", "pink_petals",
    "pitcher_plant", "sea_pickle", "rubber_leaves",
    "beetroot", "carrot", "melon_seeds",
    "brown_mushroom", "kelp", "pitcher_pod", "potato", "sweet_berries",
    "ink_sac", "music_disc", "deepslate_bricks",
    # GregTech RF/FE <-> EU energy converters - RF energy is not ported yet.
    "energy_converter",
    # Monitors (central_monitor / advanced_monitor / monitor) and their
    # casing + computer_monitor_cover - not ported. Bare `monitor` token
    # catches every variant (monitor / advanced_monitor / central_monitor /
    # monitor_casing / computer_monitor_cover).
    "monitor",
    # Charcoal pile igniter multiblock - not ported.
    "charcoal_pile_igniter",
    # Raw meats - no Terraria equivalent.
    "chicken", "rabbit", "porkchop", "mutton",
    # Melon (catches melon / melon_slice / glistering_melon_slice) and
    # compressed-ice variants - no Terraria equivalent.
    "melon", "packed_ice", "blue_ice",
    # Every GregTech coloured lamp - the normal `<colour>_lamp` plus the
    # separate `<colour>_borderless_lamp` variant ("borderless_lamp" catches
    # all 16 of those). `minecraft:redstone_lamp` is dropped too.
    "borderless_lamp", "redstone_lamp",
    "white_lamp", "orange_lamp", "magenta_lamp", "light_blue_lamp",
    "yellow_lamp", "lime_lamp", "pink_lamp", "gray_lamp", "light_gray_lamp",
    "cyan_lamp", "purple_lamp", "blue_lamp", "brown_lamp", "green_lamp",
    "red_lamp", "black_lamp",
    # Minecraft decorative stone-family wall / stairs / slab - the port keeps
    # only the base blocks (andesite -> StoneSlab, ...), not the cut variants.
    # `brick_*` also catches stone_brick_* / deepslate_brick_* / prismarine_
    # brick_*; `stone_stairs` also catches cobblestone_/sandstone_/blackstone_
    # stairs; `prismarine_*` also catches dark_prismarine_*. `stone_slab` and
    # `cobblestone_slab` are deliberately NOT listed - they resolve to
    # Terraria's Stone Slab and stay craftable.
    "andesite_wall", "andesite_stairs", "andesite_slab",
    "diorite_wall", "diorite_stairs", "diorite_slab",
    "granite_wall", "granite_stairs", "granite_slab",
    "blackstone_wall", "blackstone_slab",
    "sandstone_wall", "sandstone_slab",
    "cobblestone_wall", "stone_stairs",
    "brick_wall", "brick_stairs", "brick_slab",
    "quartz_stairs",
    # Stones / decorative blocks with no Terraria analogue. Substring tokens
    # consolidate families: `cobbled_deepslate` catches _wall/_stairs/_slab;
    # `purpur` catches _block/_pillar/_stairs/_slab; `prismarine` catches base
    # block + _bricks + dark_ + _shard + _wall/_stairs/_slab.
    "calcite", "cobbled_deepslate", "chiseled_deepslate",
    "purpur", "prismarine",
    "quartz_bricks", "quartz_pillar", "chiseled_quartz_block",
    # Nether / End / Aquatic oddities with no Terraria analogue. `nether_wart`
    # also catches nether_wart_block; `spider_eye` also catches fermented_;
    # `shulker` catches box + shell; `chorus` catches popped_chorus_fruit
    # (and any other chorus_* MC ids); `hanging_sign` catches every species.
    "nether_wart", "spider_eye", "cocoa_beans", "shulker", "chorus",
    "beehive", "hanging_sign",
    # Grindstone - no Terraria analogue (the MeatGrinder stand-in substitution
    # was removed). No gtceu id contains "grindstone", so the token is safe.
    "grindstone",
    # Duct pipes (small/normal/large/huge) - the hazard-particle transport
    # pipenet. Their sole consumer (EnvironmentalHazardEmitter/CleanerTrait +
    # the environmental hazard subsystem) is not ported.
    # `duct_pipe` catches all 4 size ids and nothing else (does not touch
    # duct_tape / nonconducting / inductor / superconducting / byproducts).
    "duct_pipe",
)

# Exact item ids to drop - matched whole, never as a substring. For each, a
# substring token would also catch a kept id:
#   minecraft:snow         within snow_block / snowball (kept)
#   minecraft:golden_apple within enchanted_golden_apple (kept -> Candy Apple)
#   minecraft:barrel       within gtceu:powderbarrel (kept casing)
#   minecraft:lead         within gtceu lead-metal items (lead_ingot, lead_dust, ...)
#   minecraft:target       within gtceu:*_laser_target_hatch (multiblock parts)
#   minecraft:allium       within gtceu gallium-metal items (gallium_ingot, ...)
#   minecraft:{wooden,stone}_<tool> - share a suffix with gtceu material tools
REMOVED_EXACT_IDS = (
    "minecraft:snow",
    "minecraft:golden_apple",
    "minecraft:barrel",
    "minecraft:map",
    "minecraft:lead",
    "minecraft:target",
    "minecraft:allium",
    "minecraft:wooden_axe", "minecraft:wooden_pickaxe", "minecraft:wooden_sword",
    "minecraft:wooden_shovel", "minecraft:wooden_hoe",
    "minecraft:stone_axe", "minecraft:stone_pickaxe", "minecraft:stone_sword",
    "minecraft:stone_shovel", "minecraft:stone_hoe",
    # Power-armor BOOTS - dropped to keep parity with Terraria's 3 armor slots
    # (head/body/legs only). Removes the boots craft + recycle recipes. The
    # Advanced chestplates are NOT dropped - they're combinational chest variants
    # of the same suit (alternate body piece, like Hallowed Mask vs Helmet).
    "gtceu:nanomuscle_boots", "gtceu:quarktech_boots",
)


def mentions_removed(recipe_id, obj):
    """True if a recipe involves any REMOVED_TOKENS (substring) or
    REMOVED_EXACT_IDS (whole-string) material/item."""
    rid = recipe_id.lower()
    if any(tok in rid for tok in REMOVED_TOKENS):
        return True

    def walk(x):
        if isinstance(x, dict):
            return any(walk(v) for v in x.values())
        if isinstance(x, list):
            return any(walk(v) for v in x)
        if not isinstance(x, str):
            return False
        s = x.lower()
        return (any(tok in s for tok in REMOVED_TOKENS)
                or s in REMOVED_EXACT_IDS)

    return walk(obj)


# GregTech generates an ore block per (host stone x material) - the same ore
# hosted in granite, deepslate, endstone, sand, ... The port keeps only the
# plain stone host; the ~2.6k alt-host ore recipes (host_<material>_ore ->
# crushed_<material>_ore) are dropped. (Was RecipeExtractor.AltStoneOreHosts
# in the retired .NET extractor - not carried over to this script until now.)
ORE_HOSTS = (
    "granite", "red_granite", "diorite", "andesite", "deepslate", "tuff",
    "marble", "basalt", "blackstone", "netherrack", "endstone",
    "sand", "red_sand", "gravel",
)


def _is_alt_host_ore_id(item_id):
    """True for an id like gtceu:granite_iron_ore - a host-prefixed ore."""
    if not (item_id.startswith("gtceu:") and item_id.endswith("_ore")):
        return False
    bare = item_id[len("gtceu:"):]
    for h in ORE_HOSTS:
        # host prefix, with a material segment still ending in _ore after it
        if bare.startswith(h + "_") and bare[len(h) + 1:].endswith("_ore"):
            return True
    return False


def is_alt_host_ore_recipe(obj):
    """True if a recipe references any alt-stone-host ore item."""
    def walk(x):
        if isinstance(x, dict):
            return any(walk(v) for v in x.values())
        if isinstance(x, list):
            return any(walk(v) for v in x)
        return isinstance(x, str) and _is_alt_host_ore_id(x)

    return walk(obj)


# Applied Energistics 2 (ME network) parts are not ported until the AE2
# integration lands. Upstream ships ME bus/hatch/pattern-buffer parts whose
# ids are all namespace-qualified `gtceu:me_*` (me_input_bus, me_output_hatch,
# me_pattern_buffer[_proxy], me_stocking_input_*, ...). We key off the
# `gtceu:me_` PREFIX, NOT a bare `me_` substring - the latter is a minefield
# (`nichrome_ingot`, `name_casting_mold`, `slime_`, `lime_` all contain
# "me_"). Any recipe that produces or consumes one of these items is dropped
# (the producing recipe references the item in its body, so the body walk
# alone catches both crafting and the research_station/assembly_line chain).
def is_ae2_recipe(obj):
    """True if a recipe references any AE2 ME part (gtceu:me_* item id)."""
    def walk(x):
        if isinstance(x, dict):
            return any(walk(v) for v in x.values())
        if isinstance(x, list):
            return any(walk(v) for v in x)
        return isinstance(x, str) and x.startswith("gtceu:me_")

    return walk(obj)


# Concrete blocks are deliberately not ported: Minecraft's 16 coloured
# concrete / concrete_powder are decorative blocks with no Terraria analogue,
# and GregTech's own light/dark concrete (+ its brick/tile/cobblestone/windmill
# decorative variants) are unported building blocks. The concrete *material*
# stays - `concrete_dust` (+ small/tiny dust, the concrete fluid bucket) is a
# real ported MaterialItem - so a string is "kept concrete" only if it is a
# dust / bucket reference; anything else mentioning "concrete" is a block.
KEEP_CONCRETE = ("concrete_dust", "dusts/concrete", "concrete_bucket")


def is_concrete_block_recipe(recipe_id, obj):
    """True if a recipe references a concrete BLOCK (MC coloured concrete /
    concrete_powder, or a GregTech light/dark concrete building block).
    Recipes touching only the concrete dust material / fluid are kept."""
    def is_block_token(s):
        return ("concrete" in s.lower()
                and not any(k in s.lower() for k in KEEP_CONCRETE))

    if is_block_token(recipe_id):
        return True

    def walk(x):
        if isinstance(x, dict):
            return any(walk(v) for v in x.values())
        if isinstance(x, list):
            return any(walk(v) for v in x)
        return isinstance(x, str) and is_block_token(x)

    return walk(obj)


# Whole recipe TYPES dropped - every recipe of this `type` is removed.
# minecraft:smithing_transform - the smithing-table netherite-upgrade recipes
# (diamond tool + netherite ingot -> netherite tool). No smithing table is
# ported; the upgrade path isn't reproduced.
REMOVED_RECIPE_TYPES = (
    "minecraft:smithing_transform",
    # Custom GT crafting types that need their own server-side click handler
    # (facade application onto a held cover, in-place tool-head swap with NBT
    # preservation). Neither is wired up in the Terraria port - they would
    # show in the recipe browser as no-op rows. Drop at extraction.
    "gtceu:crafting_facade_cover",
    "gtceu:crafting_tool_head_replace",
)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--input",  type=Path, default=DEFAULT_INPUT,
                        help=f"upstream runData recipe directory (default: {DEFAULT_INPUT.relative_to(REPO_ROOT)})")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT,
                        help=f"bundled output file (default: {DEFAULT_OUTPUT.relative_to(REPO_ROOT)})")
    parser.add_argument("--clean", action="store_true",
                        help="delete legacy per-station JSON files in the output directory after writing the bundle")
    parser.add_argument("--pretty", action="store_true",
                        help="emit pretty-printed JSON (larger file, easier to diff)")
    args = parser.parse_args()

    print(f"Scanning {args.input.relative_to(REPO_ROOT)}...")
    recipes = []
    type_counts: Counter[str] = Counter()
    dropped_wood = 0
    dropped_removed = 0
    dropped_ore_host = 0
    dropped_concrete = 0
    dropped_ae2 = 0
    dropped_recipe_type = 0
    for recipe_id, obj in walk_recipes(args.input):
        # Inject id at the top of the entry so it's the first field when
        # pretty-printed. Use a fresh dict to control key ordering.
        entry = {"id": recipe_id}
        entry.update(obj)
        # Drop whole recipe types not ported (see REMOVED_RECIPE_TYPES).
        if obj.get("type") in REMOVED_RECIPE_TYPES:
            dropped_recipe_type += 1
            continue
        # Drop non-oak wood-species variants - only the oak recipe is kept.
        if is_non_oak_wood_recipe(recipe_id):
            dropped_wood += 1
            continue
        # Drop recipes for deliberately-unported content (see REMOVED_TOKENS).
        if mentions_removed(recipe_id, obj):
            dropped_removed += 1
            continue
        # Drop alt-stone-host ore variants - only the plain stone host is kept.
        if is_alt_host_ore_recipe(obj):
            dropped_ore_host += 1
            continue
        # Drop concrete-block recipes - only the concrete dust material is kept.
        if is_concrete_block_recipe(recipe_id, obj):
            dropped_concrete += 1
            continue
        # Drop AE2 ME-part recipes until the AE2 integration is ported.
        if is_ae2_recipe(obj):
            dropped_ae2 += 1
            continue
        # Rock Crusher: strip the `adjacent_fluid` (lava + water) recipe
        # condition. The machine is ported, but its "needs lava AND water
        # adjacent" gate is NOT - sandwiching both next to a 2x2 machine is
        # impractical in Terraria's 2D world, so the recipes run unconditionally.
        if entry.get("type") == "gtceu:rock_breaker":
            entry.pop("recipeConditions", None)
        recipes.append(entry)
        recipe_type = obj.get("type", "unknown")
        # Strip "gtceu:" / "minecraft:" prefix for the histogram.
        type_counts[recipe_type.split(":", 1)[-1]] += 1

    if not recipes:
        raise SystemExit("No recipes found - check --input path.")

    if dropped_wood:
        print(f"  dropped {dropped_wood:,} non-oak wood-species recipes (kept oak only)")
    if dropped_removed:
        print(f"  dropped {dropped_removed:,} recipes for unported content ({', '.join(REMOVED_TOKENS)})")
    if dropped_ore_host:
        print(f"  dropped {dropped_ore_host:,} alt-stone-host ore recipes (kept plain stone host)")
    if dropped_concrete:
        print(f"  dropped {dropped_concrete:,} concrete-block recipes (kept concrete dust material)")
    if dropped_ae2:
        print(f"  dropped {dropped_ae2:,} AE2 ME-part recipes (gtceu:me_* - unported until AE2 lands)")
    if dropped_recipe_type:
        print(f"  dropped {dropped_recipe_type:,} recipes of unported types ({', '.join(REMOVED_RECIPE_TYPES)})")

    args.output.parent.mkdir(parents=True, exist_ok=True)
    print(f"Writing {len(recipes):,} recipes to {args.output.relative_to(REPO_ROOT)}...")
    with args.output.open("w", encoding="utf-8") as f:
        if args.pretty:
            json.dump(recipes, f, ensure_ascii=False, indent=2)
        else:
            # Compact mode - saves ~40% on disk vs pretty.
            json.dump(recipes, f, ensure_ascii=False, separators=(",", ":"))

    size_mb = args.output.stat().st_size / (1024 * 1024)
    print(f"  wrote {size_mb:.1f} MiB")

    if args.clean:
        removed = clean_legacy(args.output.parent)
        if removed > 0:
            print(f"Cleaned {removed} legacy per-station JSON files in {args.output.parent.relative_to(REPO_ROOT)}/")

    print("\nRecipes by type (top 20):")
    for recipe_type, count in type_counts.most_common(20):
        print(f"  {count:>6,}  {recipe_type}")
    if len(type_counts) > 20:
        print(f"  ... + {len(type_counts) - 20} more types")


if __name__ == "__main__":
    main()
