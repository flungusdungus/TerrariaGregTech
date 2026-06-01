#!/usr/bin/env python3
"""Single source of truth for `GregTechCEuTerraria/Localization/en-US.hjson`.

Regenerates the FULL file deterministically from:
  - Data/Materials/*.json    -> material display names + form/flag data
  - Data/Registry/items.json -> item DisplayNames: one per material/prefix item
                               upstream actually registers (dump-driven, same
                               source as MaterialItemRegistry / WireItemRegistry)
  - PREFIXES table below     -> per-prefix display templates ({0} = material)
  - Material storage blocks  -> one tile MapEntry per material with block prefix
  - Ore tiles                -> one MapEntry per material with ORE form
  - GregTech-Modern's en_us.json -> MachineTooltip section (machine
                               descriptions; optional - skipped with a warning
                               if the upstream tree isn't checked out)

Machine items, tiles, and batteries are NOT generated here - they
self-register their DisplayName/MapEntry at runtime via
`Language.GetOrRegister` in TieredMachineItem / TieredMachineTile /
BatteryItem SetStaticDefaults. Adding a new machine = writing C# only;
this script keeps working unchanged.

Idempotent - re-run after any material/prefix change. Plain-text output
(no `{$key}` substitution syntax - hjson can't parse those unquoted).
"""

import json
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MAT_DIR = os.path.join(REPO, "GregTechCEuTerraria", "Data", "Materials")
REGISTRY_ITEMS = os.path.join(REPO, "GregTechCEuTerraria", "Data", "Registry", "items.json")
OUT = os.path.join(REPO, "GregTechCEuTerraria", "Localization", "en-US.hjson")
# Upstream's generated lang file - source of the per-machine descriptions.
# Gitignored; produced by `./gradlew runData` in GregTech-Modern-1.20.1.
UPSTREAM_LANG = os.path.join(
    REPO, "GregTech-Modern-1.20.1", "src", "generated", "resources",
    "assets", "gtceu", "lang", "en_us.json")

# --- voltage tiers (mirror of VoltageTier.cs) -----------------------------
TIERS = ["ULV", "LV", "MV", "HV", "EV", "IV", "LuV", "ZPM", "UV",
         "UHV", "UEV", "UIV", "UXV", "OpV", "MAX"]

def has_form(m, f):  return f in (m.get("forms") or [])

# --- prefix table (mirror of MaterialPrefix.cs) ---------------------------
# (prefix_id, primary_upstream_id_pattern, display_template)
# The id pattern is the item's tML internal Name (== upstream id), used to
# format display strings. The localization key itself is derived dump-side -
# emit_items walks Data/Registry/items.json and joins each registered
# (material, prefix) entry against this table's display_template by prefix_id.
# Per-material predicates ("does this material have GENERATE_PLATE?") were
# stripped along with tools/gtceu-extract: the dump lists only items upstream
# actually registers, so the predicate is implicit.
PREFIXES = [
    ("raw_ore",           "raw_{m}",                "Raw {0}"),
    ("ingot",             "{m}_ingot",              "{0} Ingot"),
    ("nugget",            "{m}_nugget",             "{0} Nugget"),
    ("gem",               "{m}_gem",                "{0}"),
    ("dust",              "{m}_dust",               "{0} Dust"),
    ("small_dust",        "small_{m}_dust",         "Small Pile of {0} Dust"),
    ("tiny_dust",         "tiny_{m}_dust",          "Tiny Pile of {0} Dust"),
    ("plate",             "{m}_plate",              "{0} Plate"),
    ("double_plate",      "double_{m}_plate",       "Double {0} Plate"),
    ("dense_plate",       "dense_{m}_plate",        "Dense {0} Plate"),
    ("foil",              "{m}_foil",               "{0} Foil"),
    ("rod",               "{m}_rod",                "{0} Rod"),
    ("long_rod",          "long_{m}_rod",           "Long {0} Rod"),
    ("bolt",              "{m}_bolt",               "{0} Bolt"),
    ("screw",             "{m}_screw",              "{0} Screw"),
    ("ring",              "{m}_ring",               "{0} Ring"),
    ("round",             "{m}_round",              "{0} Round"),
    ("gear",              "{m}_gear",               "{0} Gear"),
    ("small_gear",        "small_{m}_gear",         "Small {0} Gear"),
    ("spring",            "{m}_spring",             "{0} Spring"),
    ("small_spring",      "small_{m}_spring",       "Small {0} Spring"),
    ("rotor",             "{m}_rotor",              "{0} Rotor"),
    ("fine_wire",         "fine_{m}_wire",          "Fine {0} Wire"),
    ("crushed",           "crushed_{m}_ore",        "Crushed {0} Ore"),
    ("crushed_purified",  "purified_{m}_ore",       "Purified Crushed {0} Ore"),
    ("crushed_refined",   "refined_{m}_ore",        "Refined {0} Ore"),
    ("pure_dust",         "pure_{m}_dust",          "Purified Pile of {0} Dust"),
    ("impure_dust",       "impure_{m}_dust",        "Impure Pile of {0} Dust"),
    ("chipped_gem",       "chipped_{m}_gem",        "Chipped {0}"),
    ("flawed_gem",        "flawed_{m}_gem",         "Flawed {0}"),
    ("flawless_gem",      "flawless_{m}_gem",       "Flawless {0}"),
    ("exquisite_gem",     "exquisite_{m}_gem",      "Exquisite {0}"),
    ("hot_ingot",         "hot_{m}_ingot",          "Hot {0} Ingot"),
    ("double_ingot",      "double_{m}_ingot",       "Double {0} Ingot"),
    # Wires are handled by their own emission block (one entry per size/material/type)
    ("lens",              "{m}_lens",               "{0} Lens"),
    ("turbine_blade",     "{m}_turbine_blade",      "{0} Turbine Blade"),
    ("buzz_saw_blade",    "{m}_buzz_saw_blade",     "{0} Buzzsaw Blade"),
    ("chainsaw_head",     "{m}_chainsaw_head",      "{0} Chainsaw Head"),
    ("drill_head",        "{m}_drill_head",         "{0} Drill Head"),
    ("screwdriver_tip",   "{m}_screwdriver_tip",    "{0} Screwdriver Tip"),
    ("wire_cutter_head",  "{m}_wire_cutter_head",   "{0} Wire Cutter Head"),
    ("wrench_tip",        "{m}_wrench_tip",         "{0} Wrench Tip"),
    ("planks",            "{m}_planks",             "{0} Planks"),
    ("block",             "{m}_block",              "Block of {0}"),
    ("raw_ore_block",     "raw_{m}_block",          "Block of Raw {0}"),
    ("frame",             "{m}_frame",              "{0} Frame"),
]

# --- tier-templated machines (mirror of GregTechCEuTerraria.cs) -----------
# {base_name -> human label}. One {Name}Item_{Tier} and {Name}Tile_{Tier}
# emitted per tier, plus the bare {Name}Item / {Name}Tile sentinel (autoload
# placeholder name used by the parameterless ctor; harmless to localize).
# NOTE: Machine display names are NOT generated here - `TieredMachineItem`
# and `TieredMachineTile` self-register their `Mods....Items.<name>.DisplayName`
# / `...Tiles.<name>.MapEntry` at runtime via `Language.GetOrRegister(...Label)`.
# Translators who want non-English machine names override those keys in their
# own locale file; en-US relies on the runtime fallback so the C# `Label`
# declaration is the single source of truth.

# Wire sizes registered by WireItemRegistry. (size byte, word in item-id, label word)
WIRE_SIZES = [
    (1,  "single",    None),       # plain "<Mat> Wire"
    (2,  "double",    "Double"),
    (4,  "quadruple", "Quadruple"),
    (8,  "octal",     "Octal"),
    (16, "hex",       "Hex"),
]


# --- tools (mirror of upstream item.gtceu.tool.* + GTToolType idFormat) ---
# One DisplayName per (material x GTToolType) is composed at emit time the
# same way ToolItemLoader enumerates - each material's `tool.types`. The
# format strings are upstream's `item.gtceu.tool.<name>` verbatim ("%s" ->
# "{0}"); _TOOL_ID mirrors GTToolType.idFormat (electric tools carry a tier
# prefix, and the electric wire cutter's id base is `wire_cutter`, not the
# type name `wirecutter`).
_TOOL_FORMATS = {
    "sword": "{0} Sword", "pickaxe": "{0} Pickaxe", "shovel": "{0} Shovel",
    "axe": "{0} Axe", "hoe": "{0} Hoe", "mining_hammer": "{0} Mining Hammer",
    "spade": "{0} Spade", "scythe": "{0} Scythe", "saw": "{0} Saw",
    "hammer": "{0} Hammer", "mallet": "{0} Soft Mallet", "wrench": "{0} Wrench",
    "file": "{0} File", "crowbar": "{0} Crowbar", "screwdriver": "{0} Screwdriver",
    "mortar": "{0} Mortar", "wire_cutter": "{0} Wire Cutter", "knife": "{0} Knife",
    "butchery_knife": "{0} Butchery Knife", "plunger": "{0} Plunger",
    "buzzsaw": "{0} Buzzsaw (LV)",
    "lv_drill": "{0} Drill (LV)", "mv_drill": "{0} Drill (MV)",
    "hv_drill": "{0} Drill (HV)", "ev_drill": "{0} Drill (EV)",
    "iv_drill": "{0} Drill (IV)",
    "lv_chainsaw": "{0} Chainsaw (LV)", "hv_chainsaw": "{0} Chainsaw (HV)",
    "iv_chainsaw": "{0} Chainsaw (IV)",
    "lv_wrench": "{0} Wrench (LV)", "hv_wrench": "{0} Wrench (HV)",
    "iv_wrench": "{0} Wrench (IV)",
    "lv_wirecutter": "{0} Wire Cutter (LV)", "hv_wirecutter": "{0} Wire Cutter (HV)",
    "iv_wirecutter": "{0} Wire Cutter (IV)",
    "lv_screwdriver": "{0} Screwdriver (LV)", "hv_screwdriver": "{0} Screwdriver (HV)",
    "iv_screwdriver": "{0} Screwdriver (IV)",
}
_TOOL_ID = {
    "lv_drill": "lv_{m}_drill", "mv_drill": "mv_{m}_drill", "hv_drill": "hv_{m}_drill",
    "ev_drill": "ev_{m}_drill", "iv_drill": "iv_{m}_drill",
    "lv_chainsaw": "lv_{m}_chainsaw", "hv_chainsaw": "hv_{m}_chainsaw",
    "iv_chainsaw": "iv_{m}_chainsaw",
    "lv_wrench": "lv_{m}_wrench", "hv_wrench": "hv_{m}_wrench", "iv_wrench": "iv_{m}_wrench",
    "lv_wirecutter": "lv_{m}_wire_cutter", "hv_wirecutter": "hv_{m}_wire_cutter",
    "iv_wirecutter": "iv_{m}_wire_cutter",
    "lv_screwdriver": "lv_{m}_screwdriver", "hv_screwdriver": "hv_{m}_screwdriver",
    "iv_screwdriver": "iv_{m}_screwdriver",
}


# --- helpers --------------------------------------------------------------
def humanize(snake: str) -> str:
    """`annealed_copper` -> `Annealed Copper` (mirror of MaterialItemRegistry.Humanize)."""
    out, cap = [], True
    for c in snake:
        if c == "_":
            out.append(" "); cap = True
        else:
            out.append(c.upper() if cap else c); cap = False
    return "".join(out)


def load_materials() -> dict:
    materials = {}
    for fname in sorted(os.listdir(MAT_DIR)):
        if not fname.endswith(".json"): continue
        with open(os.path.join(MAT_DIR, fname), encoding="utf-8") as f:
            data = json.load(f)
        if isinstance(data, list):
            for m in data:
                materials[m["id"]] = m
    return materials


# --- section emitters -----------------------------------------------------
def emit_materials(materials, lines, indent):
    lines.append(f"{indent}Materials: {{")
    for mid in sorted(materials):
        lines.append(f"{indent}\t{mid}: {humanize(mid)}")
    lines.append(f"{indent}}}")


# upstream TagPrefix name -> our prefix_id is camelCase<->snake_case, except for
# the handful below. Mirror of MaterialItemRegistry.PrefixNameOverrides (C#).
_PREFIX_NAME_OVERRIDES = {
    "raw_ore":          "raw",
    "crushed":          "crushedOre",
    "crushed_purified": "purifiedOre",
    "crushed_refined":  "refinedOre",
}


def _snake_to_camel(s):
    p = s.split("_")
    return p[0] + "".join(w[:1].upper() + w[1:] for w in p[1:])


def _prefix_templates():
    """upstream TagPrefix name -> display template ({0} = material name)."""
    t = {}
    for prefix_id, _id_pattern, template in PREFIXES:
        t[_PREFIX_NAME_OVERRIDES.get(prefix_id, _snake_to_camel(prefix_id))] = template
    wire_prefix  = {1: "wireGtSingle", 2: "wireGtDouble", 4: "wireGtQuadruple",
                    8: "wireGtOctal", 16: "wireGtHex"}
    cable_prefix = {1: "cableGtSingle", 2: "cableGtDouble", 4: "cableGtQuadruple",
                    8: "cableGtOctal", 16: "cableGtHex"}
    for size, _word, label in WIRE_SIZES:
        t[wire_prefix[size]]  = "{0} Wire"  if label is None else f"{label} {{0}} Wire"
        t[cable_prefix[size]] = "{0} Cable" if label is None else f"{label} {{0}} Cable"
    return t


def emit_items(materials, lines, indent):
    """Dump-driven - one DisplayName per material/prefix item upstream ACTUALLY
    registers (read from Data/Registry/items.json), keyed by the exact upstream
    id. No predicate synthesis: no phantom `copper_ingot`-style entries, and no
    real item left without a name. Mirrors MaterialItemRegistry / WireItemRegistry
    (which enumerate the same dump)."""
    lines.append(f"{indent}Items: {{")
    i2 = indent + "\t"

    def write_entry(key, display):
        lines.append(f"{i2}{key}: {{")
        lines.append(f"{i2}\tDisplayName: {display}")
        lines.append(f'{i2}\tTooltip: ""')
        lines.append(f"{i2}}}")

    with open(REGISTRY_ITEMS, encoding="utf-8") as f:
        dump = json.load(f)
    templates = _prefix_templates()

    # TagPrefixItem (ingot/dust/plate/...), MaterialPipeBlockItem (wireGt* /
    # cableGt*) and MaterialBlockItem storage blocks. pipe* prefixes and
    # ore-host stone blocks fall through `templates` unmatched and are skipped
    # - same as the C# registries. Sorted by id for deterministic output.
    for e in sorted(dump, key=lambda x: x.get("id", "")):
        eid = e.get("id", "")
        if not eid.startswith("gtceu:"):
            continue
        cls = e.get("class", "")
        is_block = cls.endswith("MaterialBlockItem") and e.get("prefix") in ("block", "frame", "rawOreBlock")
        if not (cls.endswith("TagPrefixItem") or cls.endswith("MaterialPipeBlockItem") or is_block):
            continue
        prefix, mat = e.get("prefix"), e.get("material")
        if prefix is None or mat is None or mat not in materials:
            continue
        template = templates.get(prefix)
        if template is None:
            continue
        display = template.format(humanize(mat))
        # Wires / cables get their energy tier prefixed - "MV Copper Wire"
        # rather than "Copper Wire". A UX nicety (deliberate divergence from
        # upstream); the tier is the material's cableTier, the same value
        # WireItem.BuildCell parses (falling back to ULV when absent).
        if prefix.startswith("wireGt") or prefix.startswith("cableGt"):
            display = f"{materials[mat].get('cableTier') or 'ULV'} {display}"
        write_entry(eid[len("gtceu:"):], display)

    # GregTech tools - one DisplayName per (material x GTToolType), composed
    # the same way ToolItemLoader enumerates: every material's `tool.types`
    # from materials.json. Format strings mirror upstream item.gtceu.tool.*.
    for mid in sorted(materials):
        tool = materials[mid].get("tool")
        if not tool:
            continue
        for tn in sorted(tool.get("types") or []):
            fmt = _TOOL_FORMATS.get(tn)
            if fmt is None:
                continue
            tool_id = _TOOL_ID.get(tn, "{m}_" + tn).replace("{m}", mid)
            write_entry(tool_id, fmt.format(humanize(mid)))

    # Tier-templated machine items and batteries self-register their
    # DisplayName at runtime (TieredMachineItem.SetStaticDefaults /
    # BatteryItem.SetStaticDefaults). Not emitted here.

    lines.append(f"{indent}}}")


# RecipeLogic waiting-reason strings. Keyed by the suffix of the internal
# `gtceu.recipe.*` ids that ActionResult.Fail emits; resolved at display time
# by TerrariaCompat/Machine/RecipeStatusText.cs. Granular (per-capability) by
# deliberate divergence from upstream's generic "Insufficient Inputs".
_RECIPE_STATUS = {
    "no_input":          "Missing input items",
    "no_fluid":          "Missing input fluid",
    "no_capabilities_item_in":   "No item input bus",
    "no_capabilities_item_out":  "No item output bus",
    "no_capabilities_fluid_in":  "No fluid input hatch",
    "no_capabilities_fluid_out": "No fluid output hatch",
    "no_capabilities_eu_in":     "No energy input hatch",
    "no_capabilities_eu_out":    "No energy output hatch",
    "no_capabilities_any_in":    "No input handler",
    "no_capabilities_any_out":   "No output handler",
    "output_full":       "Output slots full",
    "fluid_output_full": "Output tank full",
    "eu_too_high":       "Recipe voltage too high for this machine",
    "insufficient_eu":   "Not enough power",
    "eu_buffer_full":    "Energy buffer full",
    "circuit_mismatch":  "Circuit setting mismatch",
    "condition":         "Conditions not met",
    # Generator-multi modifier rejections (LargeTurbineMachine,
    # LargeCombustionEngineMachine) - emitted via ModifierFunction.Cancel(...)
    # so the player sees the actual missing component instead of "Insufficient
    # Inputs".
    "no_rotor":                  "Install a rotor in the rotor holder",
    "turbine_voltage_too_low":   "Rotor too weak for this recipe's voltage",
    "no_lubricant":              "Out of lubricant",
    # Fusion-reactor modifier rejections - split out from upstream's single
    # `insufficient_eu_to_start_fusion` so the player sees which fusion-specific
    # gate failed (tier / capacitor size / charge-up phase).
    "fusion_tier_too_low":       "Reactor tier too low for this recipe",
    "fusion_capacity_too_small": "Capacitor too small - install more energy hatches",
    "fusion_capacitor_charging": "Capacitor charging - waiting for startup energy",
    "insufficient_eu_to_start_fusion": "Recipe missing eu_to_start data",
}


def emit_recipe_status(lines, indent):
    """RecipeStatus section - hand-curated suffixes (gtceu.recipe.*) plus
    every gtceu.recipe_logic.* / gtceu.recipe_modifier.* entry from upstream's
    en_us.json. The C# RecipeStatusText.Resolve strips ALL three prefixes
    (gtceu.recipe. / gtceu.recipe_logic. / gtceu.recipe_modifier.) and looks
    up the remaining suffix in this single table - works because upstream's
    suffixes (insufficient_in/out/voltage/...) don't collide with our
    hand-curated ones."""
    lines.append(f"{indent}RecipeStatus: {{")
    out = dict(_RECIPE_STATUS)
    if os.path.exists(UPSTREAM_LANG):
        with open(UPSTREAM_LANG, encoding="utf-8") as f:
            lang = json.load(f)
        for k, v in lang.items():
            for pfx in ("gtceu.recipe_logic.", "gtceu.recipe_modifier."):
                if k.startswith(pfx):
                    suffix = k[len(pfx):]
                    if "." in suffix:
                        continue            # nested keys (e.g. category.*) - skip
                    out.setdefault(suffix, _mc_to_terraria(v))
    for key in sorted(out):
        lines.append(f"{indent}\t{key}: {json.dumps(out[key], ensure_ascii=False)}")
    print(f"  recipe statuses: {len(out)}")
    lines.append(f"{indent}}}")


# --- machine descriptions -------------------------------------------------
# One-line machine descriptions, mirrored from upstream's generated en_us.json.
# Upstream shows these via MetaMachine.onAddFancyInformationTooltip, keyed on
# the tier-prefixed machine id ("gtceu.machine.lv_macerator.tooltip"); a few
# special machines instead use a definition tooltipBuilder with a single bare
# id. Our MetaMachine.OnAddFancyInformationTooltip looks the entry up by
# MachineKey (tier-prefixed), falling back to the bare MachineId.
#
# Every single-key "gtceu.machine.<id>.tooltip" entry is mirrored verbatim -
# entries for machines we have not ported are harmless (never looked up), and
# adding a new machine needs no edit here: if upstream has a tooltip under a
# matching id it is already present. The numbered "<id>.tooltip.N" entries are
# NOT mirrored - upstream adds those through a per-definition tooltipBuilder,
# which MachineDefinition has no equivalent for.

# our MachineDefinitions.cs id  <-  upstream machine id, for the handful whose
# ids differ. Everything else is emitted under upstream's id verbatim.
_MACHINE_TOOLTIP_ALIASES = {
    "combustion_generator":  "combustion",
}


# Minecraft section-sign color codes -> Terraria [c/HEX:...] inline tags.
# Formatting codes (l/o/n/m/k) and reset (r) close any open color and drop.
_MC_COLORS = {
    "0": "000000", "1": "0000AA", "2": "00AA00", "3": "00AAAA",
    "4": "AA0000", "5": "AA00AA", "6": "FFAA00", "7": "AAAAAA",
    "8": "555555", "9": "5555FF", "a": "55FF55", "b": "55FFFF",
    "c": "FF5555", "d": "FF55FF", "e": "FFFF55", "f": "FFFFFF",
}


def _mc_to_terraria(s):
    """Convert §X color codes to Terraria [c/RRGGBB:...] tags."""
    out, i, open_tag = [], 0, False
    while i < len(s):
        if s[i] == "§" and i + 1 < len(s):
            code = s[i + 1].lower()
            if open_tag:
                out.append("]"); open_tag = False
            if code in _MC_COLORS:
                out.append(f"[c/{_MC_COLORS[code]}:"); open_tag = True
            i += 2
        else:
            out.append(s[i]); i += 1
    if open_tag:
        out.append("]")
    # An empty colored segment ("[c/XXXXXX:]") happens when two §X codes touch.
    text = "".join(out)
    while True:
        cleaned = re.sub(r"\[c/[0-9A-Fa-f]{6}:\]", "", text)
        if cleaned == text:
            break
        text = cleaned
    return text.strip()


def emit_machine_tooltips(lines, indent):
    lines.append(f"{indent}MachineTooltip: {{")
    if not os.path.exists(UPSTREAM_LANG):
        print(f"  WARN: {UPSTREAM_LANG} missing - MachineTooltip section empty "
              f"(run ./gradlew runData in GregTech-Modern-1.20.1)")
        lines.append(f"{indent}}}")
        return
    with open(UPSTREAM_LANG, encoding="utf-8") as f:
        lang = json.load(f)
    # Single-line tooltip:   "gtceu.machine.<id>.tooltip"        -> key "<id>"
    # Numbered tooltipBuilder lines: "gtceu.machine.<id>.tooltip.<N>"
    #                                -> key "<id>_<N>" (hjson uses '.' for nesting,
    #                                  so flatten with '_'; the C# side mirrors).
    #
    # Entries with `%s` / `%d` placeholders are emitted RAW - they're consumed by
    # MachineDefinition.TooltipBuilder (per-def) or the MachineTooltipBuilders
    # fallback table, which substitutes the runtime value via `string.Format`.
    # The lookup helper drops any raw entry that still has placeholders after
    # the builder runs, so no `%s` ever reaches the player.
    prefix, suffix = "gtceu.machine.", ".tooltip"
    out = {}
    n_numbered = 0
    for k, v in lang.items():
        if not k.startswith(prefix):
            continue
        rest = k[len(prefix):]
        if rest.endswith(suffix):
            mid = rest[:-len(suffix)]
            if not mid or "." in mid:
                continue
            # `available_recipe_map_N` are shared templates upstream applies to
            # multis with N selectable recipe types ("Available Recipe Types:
            # X, Y, Z"). Emitted into MachineTooltip alongside per-machine
            # entries; MachineTooltipLookup appends them automatically to any
            # multi def whose RecipeTypes.Length > 1, in addition to the
            # ActiveRecipeType cycle UI we ship.
            out[_MACHINE_TOOLTIP_ALIASES.get(mid, mid)] = _mc_to_terraria(v)
            continue
        m = re.fullmatch(r"([a-z0-9_]+)\.tooltip\.(\d+)", rest)
        if not m:
            continue
        mid, n = m.group(1), int(m.group(2))
        mid = _MACHINE_TOOLTIP_ALIASES.get(mid, mid)
        out[f"{mid}_{n}"] = _mc_to_terraria(v)
        n_numbered += 1
    for mid in sorted(out):
        lines.append(f"{indent}\t{mid}: {json.dumps(out[mid], ensure_ascii=False)}")
    print(f"  machine tooltips: {len(out) - n_numbered} single + {n_numbered} numbered")
    lines.append(f"{indent}}}")


# Recipe-type display names - `gtceu.<id>` single-segment lang keys (e.g.
# `gtceu.centrifuge` = "Centrifuge"). Used by MachineTooltipLookup to fill the
# `available_recipe_map_N` template ("Available Recipe Types: X, Y") on multis
# with `RecipeTypes.Length > 1`. Mirroring the whole single-segment family is
# safe - upstream uses this namespace exclusively for recipe-map display.
def emit_recipe_type_names(lines, indent):
    lines.append(f"{indent}RecipeTypeName: {{")
    if not os.path.exists(UPSTREAM_LANG):
        lines.append(f"{indent}}}"); return
    with open(UPSTREAM_LANG, encoding="utf-8") as f:
        lang = json.load(f)
    pat = re.compile(r"^gtceu\.[a-z][a-z0-9_]*$")
    out = {}
    for k, v in lang.items():
        if pat.match(k):
            out[k[len("gtceu."):]] = _mc_to_terraria(v)
    for rt in sorted(out):
        lines.append(f"{indent}\t{rt}: {json.dumps(out[rt], ensure_ascii=False)}")
    print(f"  recipe type names: {len(out)}")
    lines.append(f"{indent}}}")


# --- Terraria-side UI strings that have no runtime fallback --------------
# tML's ModConfig UI reads labels/tooltips from hjson; GTConfig.cs has no
# [Label]/[Tooltip] attributes, so missing entries surface as raw field
# names in the in-game config menu. BossDropCondition.cs likewise uses
# Language.GetTextValue (no fallback) for its Bestiary drop-condition line.
#
# Everything else previously committed to hjson (bag items, multiblock bag
# entries, projectiles, keybinds, TooManyItemsItem tooltip) is covered by
# Language.GetOrRegister calls in source or by tML's PascalCase default
# DisplayName, so it doesn't need to be emitted here.
_CONFIGS = {
    "GTConfig": {
        "DisplayName": "GregTech",
        "EnableBossDrops": {
            "Label":   "Enable GregTech boss drops",
            "Tooltip": "If enabled, vanilla bosses drop tier-appropriate "
                       "GregTech raw ores, dusts, and circuit components.",
        },
        "SimulationSpeed": {
            "Label":   "Simulation Speed",
            "Tooltip": "",
        },
        "NetworkSyncPeriod": {
            "Label":   "Network Sync Period",
            "Tooltip": "",
        },
    },
}

_BOSSDROPS_CONDITION_DESCRIPTION = (
    "Requires boss drops enabled in config."
)


def emit_configs(lines, indent):
    lines.append(f"{indent}Configs: {{")
    i2 = indent + "\t"
    for cfg_name in sorted(_CONFIGS):
        cfg = _CONFIGS[cfg_name]
        lines.append(f"{i2}{cfg_name}: {{")
        i3 = i2 + "\t"
        if "DisplayName" in cfg:
            lines.append(f"{i3}DisplayName: {cfg['DisplayName']}")
        for field in sorted(k for k in cfg if k != "DisplayName"):
            entry = cfg[field]
            lines.append(f"{i3}{field}: {{")
            lines.append(f"{i3}\tLabel: {json.dumps(entry['Label'], ensure_ascii=False)}")
            lines.append(f"{i3}\tTooltip: {json.dumps(entry['Tooltip'], ensure_ascii=False)}")
            lines.append(f"{i3}}}")
        lines.append(f"{i2}}}")
    lines.append(f"{indent}}}")


def emit_bossdrops(lines, indent):
    desc = json.dumps(_BOSSDROPS_CONDITION_DESCRIPTION, ensure_ascii=False)
    lines.append(f"{indent}BossDrops.ConditionDescription: {desc}")


def emit_tiles(materials, lines, indent):
    lines.append(f"{indent}Tiles: {{")
    i2 = indent + "\t"

    # All tiles use upstream-id Names: <mat>_block, <mat>_ore, <tier>_<machine>.
    # 1) Material storage blocks - emitted for materials with INGOT or GEM form
    # (mirror of MaterialPrefix.cs `block` predicate). MaterialBlockItem's
    # placement check upstream is the same form gate.
    for mid in sorted(materials):
        m = materials[mid]
        if not (has_form(m, "INGOT") or has_form(m, "GEM")): continue
        lines.append(f"{i2}{mid}_block.MapEntry: Block of {humanize(mid)}")

    # 2) Ore tiles - one per material with the ORE form.
    for mid in sorted(materials):
        if not has_form(materials[mid], "ORE"): continue
        lines.append(f"{i2}{mid}_ore.MapEntry: {humanize(mid)} Ore")

    # Tier-templated machine tiles self-register their MapEntry at runtime
    # via TieredMachineTile.SetStaticDefaults - nothing to emit here.

    lines.append(f"{indent}}}")


def main():
    materials = load_materials()
    print(f"loaded {len(materials)} materials")

    lines = ["Mods: {", "\tGregTechCEuTerraria: {"]
    indent = "\t\t"

    emit_materials(materials, lines, indent)
    emit_items(materials, lines, indent)
    emit_tiles(materials, lines, indent)
    emit_recipe_status(lines, indent)
    emit_machine_tooltips(lines, indent)
    emit_recipe_type_names(lines, indent)
    emit_configs(lines, indent)
    emit_bossdrops(lines, indent)

    lines.append("\t}")
    lines.append("}")
    lines.append("")  # trailing newline

    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))

    # Quick stats for sanity check
    n_items = sum(1 for L in lines if L.endswith(": {") and "\t\t\t" in L)
    print(f"wrote {OUT}  ({len(lines)} lines, ~{n_items} item entries)")


if __name__ == "__main__":
    sys.exit(main() or 0)
