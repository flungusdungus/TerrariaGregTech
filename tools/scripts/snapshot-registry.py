#!/usr/bin/env python3
"""
Snapshot the upstream item-registry dump into our data tree.

`./gradlew runData` in `GregTech-Modern-1.20.1/` runs the registry-dump
DataProvider (patched into DataGenerators.java) and writes the authoritative
item catalog - every registered item's id, concrete Java class, and intrinsic
data - to `src/generated/resources/registry/items.json`.

This script copies that into `GregTechCEuTerraria/Data/Registry/items.json`,
joining each entry with its display name from the generated upstream lang file
(`assets/gtceu/lang/en_us.json`). The mod's RegistryItemLoader reads the result
at Mod.Load. Replaces source-regex extraction (GTItems.java) with a reliable
runtime dump.

Workflow:
  1. In `GregTech-Modern-1.20.1/`, run `./gradlew runData`.
  2. python tools/scripts/snapshot-registry.py
"""
from __future__ import annotations

import json
import os
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
GEN = REPO / "GregTech-Modern-1.20.1" / "src" / "generated" / "resources"
SRC_ITEMS = GEN / "registry" / "items.json"
SRC_MATERIALS = GEN / "registry" / "materials.json"
SRC_MAT_TAGS = GEN / "registry" / "material_item_tags.json"
SRC_VEINS = GEN / "registry" / "veins.json"
SRC_LANG  = GEN / "assets" / "gtceu" / "lang" / "en_us.json"
SRC_MODELS = GEN / "assets" / "gtceu" / "models" / "block"
SRC_BLOCKSTATES = GEN / "assets" / "gtceu" / "blockstates"
# Some block models are hand-authored in `src/main` rather than emitted by
# datagen (e.g. `block/hermetic_casing`, `block/computer_casing`, every
# `block/variant/*` active-state model). The generated-resources tree
# inherits them at runtime via the resource pack stack; for the script we
# fall back to src/main/ when the generated tree doesn't have the file.
SRC_MAIN_MODELS = REPO / "GregTech-Modern-1.20.1" / "src" / "main" / "resources" / "assets" / "gtceu" / "models" / "block"
SRC_DATA  = GEN / "data"
OUT       = REPO / "GregTechCEuTerraria" / "Data" / "Registry" / "items.json"
OUT_TAGS  = REPO / "GregTechCEuTerraria" / "Data" / "Registry" / "tags.json"
OUT_FLUID_TAGS = REPO / "GregTechCEuTerraria" / "Data" / "Registry" / "fluid_tags.json"
OUT_MATERIALS = REPO / "GregTechCEuTerraria" / "Data" / "Materials" / "materials.json"
OUT_VEINS = REPO / "GregTechCEuTerraria" / "Data" / "Veins" / "veins.json"

# Namespaces whose datagen tags we snapshot. gtceu = the mod's own tags
# (circuits, batteries, ...); forge / minecraft = the cross-mod convention tags
# (dyes, rods, planks, ...). NB: GregTech's material-prefix tags (forge:ingots/*,
# forge:dusts/*, ...) are NOT datagen JSON - they are attached at item
# registration time - so they are reconstructed C#-side from MaterialItemRegistry,
# not snapshotted here.
TAG_NAMESPACES = ("gtceu", "forge", "minecraft")


def load_lang() -> dict[str, str]:
    """item resource-id -> display name, from the generated en_us.json.

    Lang keys are `item.<namespace>.<path>` or `block.<namespace>.<path>`; we
    re-key to `<namespace>:<path>` to match the dump's item ids. A placeable
    block's BlockItem has no `item.*` key - it inherits the `block.*` name - so
    both prefixes are read, with `item.*` winning on conflict. `.tooltip` (and
    other dotted sub-keys) are skipped - only the bare name."""
    if not SRC_LANG.is_file():
        print(f"  note: lang file not found ({SRC_LANG.name}) - names will be humanized")
        return {}
    raw = json.loads(SRC_LANG.read_text(encoding="utf-8"))
    names: dict[str, str] = {}
    # `block.` first, `item.` second so an item with its own name overrides.
    for prefix in ("block.", "item."):
        for key, value in raw.items():
            if not key.startswith(prefix):
                continue
            rest = key[len(prefix):]         # "<namespace>.<path>[.tooltip]"
            if rest.count(".") != 1:         # skip .tooltip etc.
                continue
            namespace, path = rest.split(".", 1)
            names[f"{namespace}:{path}"] = value
    return names


# --- Block-model resolution -------------------------------------------------
#
# Identify gtceu BlockItems that are "simple full cubes" - those port as a
# plain CasingTile. Three classes of cube-ish parent are accepted:
#   * any parent containing "cube" (cube_all, cube_bottom_top, cube_column, ...)
#   * `block/block` - bare base used by hand-authored full-cube models
#     made of 0->16 elements (hermetic_casing family)
#   * `block/orientable*` - full cube with per-face textures (computer_casing,
#     advanced_computer_casing, ...)
#
# Doors / fences / slabs / stairs / saplings / leaves / buttons / pressure
# plates / trapdoors / fence gates have parents that match NONE of the above
# and are silently dropped.

_CUBE_PARENT_PREFIXES = ("block/orientable", "minecraft:block/orientable")
_CUBE_PARENTS_EXACT   = {"block/block", "minecraft:block/block"}

def _parent_is_cube_direct(parent: str) -> bool:
    p = (parent or "").lower()
    if "cube" in p: return True
    if p in _CUBE_PARENTS_EXACT: return True
    return p.startswith(_CUBE_PARENT_PREFIXES)


def _model_is_cube(model: dict, depth: int = 0) -> bool:
    """Walk the model's parent chain (gtceu and vanilla) until we hit a cube-
    shaped ancestor or run out. `lv_hermetic_casing` -> `gtceu:block/hermetic_
    casing` -> `block/block` (cube); `advanced_computer_casing` -> `gtceu:block/
    computer_casing` -> `block/orientable_with_bottom` (cube)."""
    if model is None or depth > 4: return False
    parent = model.get("parent") or ""
    if _parent_is_cube_direct(parent): return True
    if not parent.startswith("gtceu:"): return False
    return _model_is_cube(_load_model(parent), depth + 1)


def _load_json(path: Path) -> dict | None:
    if not path.is_file(): return None
    try: return json.loads(path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError): return None


def _load_model(model_ref: str) -> dict | None:
    """Resolve a model ref like `gtceu:block/variant/crushing_wheels` to its
    JSON content. Tries the generated tree first, then src/main (some block
    models are hand-authored and inherited via the resource pack stack)."""
    rel = model_ref[len("gtceu:"):] if model_ref.startswith("gtceu:") else model_ref
    # rel is "block/<sub>/<id>"; strip the leading "block/" to get path under
    # SRC_MODELS / SRC_MAIN_MODELS, which already point at .../models/block.
    rel = rel[len("block/"):] if rel.startswith("block/") else rel
    return _load_json(SRC_MODELS / f"{rel}.json") or _load_json(SRC_MAIN_MODELS / f"{rel}.json")


def _pick_face_texture(textures: dict, prefer_basename: str | None = None,
                       prefer_suffix: tuple[str, ...] = ()) -> str | None:
    """From a model's `textures` dict, pick the gtceu-namespaced face texture
    best representing the block viewed flat.

    Preference order:
      1. Any texture whose path basename matches `prefer_basename` (so a
         block named `crushing_wheels` picks its `.../unique/crushing_wheels`
         texture instead of the shared `secure_maceration_casing` side, and
         active variants pick `crushing_wheels_active` over the shared side).
      2. Any key containing `side` (hermetic casings use `bot_side` for the
         voltage-tier face).
      3. Explicit-priority keys (`all`/`end`/`top`/`particle`).
      4. Any gtceu-namespaced texture.
    """
    def _gtceu_vals() -> list[tuple[str, str]]:
        return [(k, v) for k, v in textures.items()
                if isinstance(v, str) and v.startswith("gtceu:")]

    if prefer_basename:
        for _k, v in _gtceu_vals():
            if v.rsplit("/", 1)[-1] == prefer_basename:
                return v[len("gtceu:"):]
    if prefer_suffix:
        for _k, v in _gtceu_vals():
            base = v.rsplit("/", 1)[-1]
            if any(base.endswith(s) for s in prefer_suffix):
                return v[len("gtceu:"):]
    for k, v in textures.items():
        if isinstance(v, str) and v.startswith("gtceu:") and "side" in k.lower():
            return v[len("gtceu:"):]
    for k in ("all", "end", "top", "particle"):
        v = textures.get(k)
        if isinstance(v, str) and v.startswith("gtceu:"):
            return v[len("gtceu:"):]
    for _k, v in _gtceu_vals():
        return v[len("gtceu:"):]
    return None


def _resolve_variant_model(bare_id: str, want_active: bool) -> dict | None:
    """When `block/<id>.json` doesn't exist, fall back to the blockstate's
    variant model. `want_active=True` picks the `active=true` variant if
    present (used to emit the active-overlay face); else the inactive /
    default variant."""
    bs = _load_json(SRC_BLOCKSTATES / f"{bare_id}.json")
    if bs is None: return None
    variants = bs.get("variants")
    if not isinstance(variants, dict) or not variants: return None
    # Prefer an explicit active/!active variant when the blockstate has them.
    chosen_ref: str | None = None
    for key, val in variants.items():
        if "active=" not in key: continue
        if (want_active and "active=true" in key) or (not want_active and "active=false" in key):
            v = val[0] if isinstance(val, list) else val
            if isinstance(v, dict): chosen_ref = v.get("model")
            break
    if chosen_ref is None:
        if want_active: return None  # blockstate has no active=true -> no active texture
        # No active toggle in this blockstate - pick the first variant.
        v = next(iter(variants.values()))
        if isinstance(v, list): v = v[0]
        if isinstance(v, dict): chosen_ref = v.get("model")
    if not isinstance(chosen_ref, str): return None
    return _load_model(chosen_ref)


def load_block_render(bare_id: str) -> tuple[str | None, str | None]:
    """For a gtceu BlockItem, return (inactiveTexture, activeTexture) - both
    gtceu-relative paths (namespace stripped, e.g. `block/casings/...`). The
    active texture is non-None only when the blockstate has an `active=true`
    variant (i.e. the block is a controller-driven multiblock part casing)."""
    # First pass: the model at `models/block/<id>.json`. Most casings live here.
    model = _load_json(SRC_MODELS / f"{bare_id}.json") or _load_json(SRC_MAIN_MODELS / f"{bare_id}.json")
    if model is None or not _model_is_cube(model):
        # Fall back to the blockstate-driven variant model.
        model = _resolve_variant_model(bare_id, want_active=False)
    if model is None or not _model_is_cube(model):
        return (None, None)

    inactive = _pick_face_texture(model.get("textures") or {}, prefer_basename=bare_id)
    if inactive is None: return (None, None)

    # Active variant (optional). Only blocks with `active=true` get one. The
    # distinctive face is the one whose basename matches `<id>_active`.
    active: str | None = None
    active_model = _resolve_variant_model(bare_id, want_active=True)
    if active_model is not None and _model_is_cube(active_model):
        # Active-state textures use one of three upstream conventions:
        #   <id>_active   (face-swap models - crushing_wheels, slicing_blades)
        #   <id>_bloom    (cube_2_layer additive overlay - heat_vent, MoSi2 coil)
        #   anything ending in _active / _bloom on any other key.
        active = _pick_face_texture(active_model.get("textures") or {},
                                    prefer_basename=f"{bare_id}_active",
                                    prefer_suffix=("_active", "_bloom"))

    return (inactive, active)


def load_tags(kind: str) -> dict[str, list[str]]:
    """tag id -> member list, for `data/<ns>/tags/<kind>/` across all
    TAG_NAMESPACES. `kind` is "items" or "fluids".

    Tag id is `<ns>:<relpath>` (no .json) - `forge/tags/items/dyes/red.json`
    -> `forge:dyes/red`. Members are ids or `#`-prefixed nested-tag refs;
    object-form `{"id":...,"required":...}` entries are flattened to the bare
    id. The C# RegistryTagLoader expands the nested refs."""
    tags: dict[str, list[str]] = {}
    for ns in TAG_NAMESPACES:
        root = SRC_DATA / ns / "tags" / kind
        if not root.is_dir():
            continue
        for f in sorted(root.rglob("*.json")):
            rel = f.relative_to(root).with_suffix("")
            tag_id = f"{ns}:" + str(rel).replace(os.sep, "/")
            data = json.loads(f.read_text(encoding="utf-8"))
            members: list[str] = []
            for v in data.get("values", []):
                if isinstance(v, str):
                    members.append(v)
                elif isinstance(v, dict) and isinstance(v.get("id"), str):
                    members.append(v["id"])
            tags[tag_id] = members
    return tags


def main() -> None:
    if not SRC_ITEMS.is_file():
        raise SystemExit(
            f"Registry dump not found: {SRC_ITEMS}\n"
            "Run `./gradlew runData` in GregTech-Modern-1.20.1/ first "
            "(the registry-dump DataProvider patched into DataGenerators.java).")

    items = json.loads(SRC_ITEMS.read_text(encoding="utf-8"))
    if not isinstance(items, list) or not items:
        raise SystemExit(f"Registry dump is empty or not a JSON array: {SRC_ITEMS}")

    names = load_lang()
    named = 0
    for it in items:
        n = names.get(it.get("id", ""))
        if n is not None:
            it["name"] = n
            named += 1

    # Tag every gtceu cube BlockItem with its block texture, read from the
    # generated block model. Drives the dump-driven CasingRegistry (one
    # placeable 2x2 casing tile per entry). Non-cube blocks (doors / fences /
    # slabs / stairs / ...) get no `render` and are skipped by the registry.
    cube_blocks = 0
    active_blocks = 0
    for it in items:
        iid = it.get("id", "")
        if not iid.startswith("gtceu:"):
            continue
        if it.get("class", "").rsplit(".", 1)[-1] != "BlockItem":
            continue
        inactive, active = load_block_render(iid[len("gtceu:"):])
        if inactive is None:
            continue
        render: dict = {"texture": inactive}
        if active is not None and active != inactive:
            render["activeTexture"] = active
            active_blocks += 1
        it["render"] = render
        cube_blocks += 1

    OUT.parent.mkdir(parents=True, exist_ok=True)
    with OUT.open("w", encoding="utf-8", newline="\n") as f:
        json.dump(items, f, ensure_ascii=False, separators=(",", ":"))

    classes = sorted({it.get("class", "?") for it in items})
    print(f"snapshotted {len(items):,} items -> {OUT.relative_to(REPO)}")
    print(f"  {len(classes)} distinct item classes, {named:,} with display names")
    print(f"  {cube_blocks:,} cube BlockItems tagged with a render texture "
          f"({active_blocks:,} with an active-overlay variant)")

    tags = load_tags("items")

    # Merge GregTech's material-prefix item tags (forge:ingots/iron, ...). These
    # are attached at item-registration time, never written as datagen JSON, so
    # the Material Item-Tag DataProvider dumps them off the live registry.
    if SRC_MAT_TAGS.is_file():
        mat_tags = json.loads(SRC_MAT_TAGS.read_text(encoding="utf-8"))
        for tag_id, members in mat_tags.items():
            existing = tags.setdefault(tag_id, [])
            for m in members:
                if m not in existing:
                    existing.append(m)
        print(f"  merged {len(mat_tags):,} material item tags from {SRC_MAT_TAGS.name}")
    else:
        print(f"  note: material tag dump not found ({SRC_MAT_TAGS.name})")

    # Circuit-tier fallback. GregTech declares a `gtceu:circuits/<tier>` tag for
    # every voltage tier (CustomTags.java), but the top tiers (uev, uiv, uxv,
    # opv, max) have no circuit item - there is no circuit above the uhv-tier
    # `wetware_processor_mainframe` - so their datagen tag file is never
    # emitted. Recipes for UEV+ machines still reference those tags (e.g.
    # `uiv_circuit_assembler` wants `#gtceu:circuits/uxv`). Fill each empty
    # high tier with the highest populated tier below it so the tag resolves
    # to "the best circuit available".
    circuit_tiers = ["ulv", "lv", "mv", "hv", "ev", "iv", "luv",
                     "zpm", "uv", "uhv", "uev", "uiv", "uxv", "opv", "max"]
    last_circuits: list[str] = []
    filled = 0
    for tier in circuit_tiers:
        tag_id = f"gtceu:circuits/{tier}"
        members = tags.get(tag_id)
        if members:
            last_circuits = members
        elif last_circuits:
            tags[tag_id] = list(last_circuits)
            filled += 1
    if filled:
        print(f"  filled {filled} empty high-tier circuit tags "
              f"(fallback to highest populated tier)")

    with OUT_TAGS.open("w", encoding="utf-8", newline="\n") as f:
        json.dump(tags, f, ensure_ascii=False, separators=(",", ":"))
    print(f"snapshotted {len(tags):,} item tags -> {OUT_TAGS.relative_to(REPO)}")

    fluid_tags = load_tags("fluids")
    with OUT_FLUID_TAGS.open("w", encoding="utf-8", newline="\n") as f:
        json.dump(fluid_tags, f, ensure_ascii=False, separators=(",", ":"))
    print(f"snapshotted {len(fluid_tags):,} fluid tags -> {OUT_FLUID_TAGS.relative_to(REPO)}")

    # Material chemistry dump - the materials DataProvider reads the live
    # registry, so this replaces the regex-extracted Data/Materials/*.json.
    # Wipe the old themed files; the single materials.json is authoritative.
    if SRC_MATERIALS.is_file():
        materials = json.loads(SRC_MATERIALS.read_text(encoding="utf-8"))
        OUT_MATERIALS.parent.mkdir(parents=True, exist_ok=True)
        for stale in OUT_MATERIALS.parent.glob("*.json"):
            if stale != OUT_MATERIALS:
                stale.unlink()
        with OUT_MATERIALS.open("w", encoding="utf-8", newline="\n") as f:
            json.dump(materials, f, ensure_ascii=False, separators=(",", ":"))
        print(f"snapshotted {len(materials):,} materials -> {OUT_MATERIALS.relative_to(REPO)}")
    else:
        print(f"  note: material dump not found ({SRC_MATERIALS.name}) - materials unchanged")

    # Ore vein dump - the OreVeinDataProvider walks GTRegistries.ORE_VEINS at
    # runData time so the dump tracks the live registry (no regex parse of
    # GTOres.java, no MaterialAliasResolver to hand-maintain). Schema matches
    # what the retired tools/gtceu-extract veins extractor emitted.
    if SRC_VEINS.is_file():
        veins = json.loads(SRC_VEINS.read_text(encoding="utf-8"))
        OUT_VEINS.parent.mkdir(parents=True, exist_ok=True)
        with OUT_VEINS.open("w", encoding="utf-8", newline="\n") as f:
            json.dump(veins, f, ensure_ascii=False, indent=2)
        print(f"snapshotted {len(veins):,} ore veins -> {OUT_VEINS.relative_to(REPO)}")
    else:
        print(f"  note: vein dump not found ({SRC_VEINS.name}) - veins unchanged")


if __name__ == "__main__":
    main()
