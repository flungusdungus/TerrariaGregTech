#!/usr/bin/env python3
"""
Port the GregTech Modern Community Pack questbook into a data-driven quest log.

Source: the pack's FTB Quests data (config/ftbquests/quests/, SNBT format).
The mod ships its OWN minimal quest log -- no QuestBooks dependency -- with
exactly two task kinds: `item` (collect a resource) and `checkmark` (manual).
FTB's other task types (choice / dimension / command / ...) degrade to
checkmark.

Re-runnable and non-destructive: regenerates only the two output files,
touches nothing else.

Outputs:
  GregTechCEuTerraria/Data/Questbook/questlog.json
      chapters -> quests -> tasks. Flat (no canvas x/y). Item ids stay raw
      (gtceu:* / minecraft:*); the in-mod loader resolves them through the
      universal IngredientResolverImpl, same as recipes.
  GregTechCEuTerraria/Localization/en-US_Questbook.hjson
      chapter names + quest titles + descriptions.

FTB SNBT is NBT-as-text: unquoted keys, no separating commas (newline-
separated), numbers with type suffixes (16L, -2.5d, 1b). SnbtParser handles it.

Usage:
  python tools/scripts/port-questbook.py [path/to/config/ftbquests/quests]
"""
from __future__ import annotations

import json
import re
import sys
from collections import Counter
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
# Path to a checkout / extracted copy of
# https://github.com/GregTechCEu/GregTech-Modern-Community-Pack
# Set GT_COMMUNITY_PACK_DIR or pass it as argv[1]. Inside that dir, the
# script reads `config/ftbquests/quests/`.
DEFAULT_SRC = None

OUT_DIR = REPO / "GregTechCEuTerraria" / "Data" / "Questbook"
QUESTLOG = OUT_DIR / "questlog.json"

# Minecraft / FTB formatting code: `&` (or section sign) + one code char.
_CODE_RE = re.compile(r"[&§][0-9A-Fa-fK-ORk-or]")

# Modded namespaces present in the source pack but not in scope for the
# Terraria port -- any quest whose icon or task items/tag reference one of
# these is dropped (and its chapter nodes pruned). Keep `ae2` -- it's planned.
# `kubejs` / `ftbquests` only appear as decorative icons on otherwise-generic
# quests, so they're allowed; the icon harmlessly fails to resolve.
DROP_NAMESPACES = {
    "craftingstation",
    "hangglider",
    "toolbelt",
    "sophisticatedbackpacks",
    "buildinggadgets2",
    "expatternprovider",
    "travelanchors",
    "javd",
    "mae2",
    "ae2wtlib",
}


def quest_namespaces(icon: str, tasks: list) -> set:
    ns = set()
    if icon and ":" in icon:
        ns.add(icon.split(":", 1)[0])
    for t in tasks:
        for it in t.get("items", []) or []:
            if ":" in it:
                ns.add(it.split(":", 1)[0])
        tag = t.get("tag", "")
        if tag and ":" in tag:
            ns.add(tag.split(":", 1)[0])
    return ns


# ---------------------------------------------------------------------------
#  SNBT parser
# ---------------------------------------------------------------------------
class SnbtParser:
    """Recursive-descent parser for the SNBT subset FTB Quests emits."""

    def __init__(self, text: str):
        self.s = text
        self.i = 0
        self.n = len(text)

    def parse(self):
        self._ws()
        return self._value()

    def _ws(self):
        # Whitespace AND commas are separators in FTB's SNBT.
        while self.i < self.n and self.s[self.i] in " \t\r\n,":
            self.i += 1

    def _value(self):
        c = self.s[self.i]
        if c == "{":
            return self._object()
        if c == "[":
            return self._list()
        if c in "\"'":
            return self._string()
        return self._primitive()

    def _object(self):
        self.i += 1  # consume {
        obj = {}
        while True:
            self._ws()
            if self.s[self.i] == "}":
                self.i += 1
                return obj
            key = self._key()
            self._ws()
            assert self.s[self.i] == ":", f"expected ':' at {self.i}"
            self.i += 1
            self._ws()
            obj[key] = self._value()

    def _list(self):
        self.i += 1  # consume [
        self._ws()
        # Typed-array prefix: [I; ...], [B; ...], [L; ...]
        if self.i + 1 < self.n and self.s[self.i].isalpha() and self.s[self.i + 1] == ";":
            self.i += 2
        out = []
        while True:
            self._ws()
            if self.s[self.i] == "]":
                self.i += 1
                return out
            out.append(self._value())

    def _key(self):
        if self.s[self.i] in "\"'":
            return self._string()
        j = self.i
        while self.i < self.n and (self.s[self.i].isalnum() or self.s[self.i] in "_-+."):
            self.i += 1
        return self.s[j:self.i]

    def _string(self):
        quote = self.s[self.i]
        self.i += 1
        out = []
        while self.s[self.i] != quote:
            c = self.s[self.i]
            if c == "\\":
                self.i += 1
                esc = self.s[self.i]
                out.append({"n": "\n", "t": "\t", "r": "\r"}.get(esc, esc))
            else:
                out.append(c)
            self.i += 1
        self.i += 1  # closing quote
        return "".join(out)

    def _primitive(self):
        j = self.i
        while self.i < self.n and self.s[self.i] not in " \t\r\n,{}[]:":
            self.i += 1
        tok = self.s[j:self.i]
        if tok == "true":
            return True
        if tok == "false":
            return False
        num = tok[:-1] if tok and tok[-1] in "dDfFlLbBsS" else tok
        try:
            if any(ch in num for ch in ".eE"):
                return float(num)
            return int(num)
        except ValueError:
            return tok  # bare unquoted string


def parse_snbt(path: Path):
    return SnbtParser(path.read_text(encoding="utf-8")).parse()


# ---------------------------------------------------------------------------
#  Helpers
# ---------------------------------------------------------------------------
def clean(text: str) -> str:
    return _CODE_RE.sub("", text or "")


def join_desc(desc) -> str:
    if isinstance(desc, list):
        return "\n".join(clean(str(line)) for line in desc)
    return clean(str(desc or ""))


def prettify(item_id: str) -> str:
    """gtceu:cupronickel_ingot -> 'Cupronickel Ingot' (title fallback)."""
    bare = item_id.split(":", 1)[-1]
    return " ".join(w.capitalize() for w in bare.split("_")) or "Quest"


def task_items(task: dict):
    """Returns (item-id list, tag-or-empty, label) for an item task.

    Handles the FTB Item Filters wrappers used as task targets:
      * `itemfilters:or`  -> tag.items is the list of acceptable items.
      * `itemfilters:tag` -> tag.value is the item-tag id (resolved later).
    A normal item task returns a single-element list. `label` is the FTB
    task `title` (e.g. "Any Logs", "Iron-bearing Ores") for nicer display.
    """
    item = task.get("item")
    label = clean(task.get("title", ""))
    if isinstance(item, str):
        return ([item] if item else []), "", label
    if isinstance(item, dict):
        iid = item.get("id", "") or ""
        nbt = item.get("tag", {}) or {}
        if iid in ("itemfilters:or", "itemfilters:and"):
            ids = [str(sub.get("id", "")) for sub in (nbt.get("items") or []) if sub.get("id")]
            return ids, "", label
        if iid == "itemfilters:tag":
            return [], str(nbt.get("value", "") or ""), label
        return ([iid] if iid else []), "", label
    return [], "", label


def task_count(task: dict) -> int:
    if "count" in task:
        return int(task["count"])
    item = task.get("item")
    if isinstance(item, dict):
        for k in ("Count", "count"):
            if k in item:
                return int(item[k])
    return 1


def quest_icon(quest: dict, raw_tasks) -> str:
    """The node icon: an explicit FTB `icon`, else the first item task's item."""
    icon = quest.get("icon")
    if isinstance(icon, str) and icon:
        return icon
    if isinstance(icon, dict):
        return icon.get("id", "") or icon.get("item", "") or ""
    for t in raw_tasks:
        if t.get("type") == "item":
            items, _, _ = task_items(t)
            if items:
                return items[0]
    return ""


# ---------------------------------------------------------------------------
#  Main
# ---------------------------------------------------------------------------
def main():
    import os
    if len(sys.argv) > 1:
        root = Path(sys.argv[1])
    elif (env := os.environ.get("GT_COMMUNITY_PACK_DIR")):
        root = Path(env)
    else:
        sys.exit(
            "Pass the GregTech Modern Community Pack checkout dir as argv[1] "
            "or set GT_COMMUNITY_PACK_DIR. Repo: "
            "https://github.com/GregTechCEu/GregTech-Modern-Community-Pack"
        )
    # Accept either the pack root or the quests dir directly.
    src = root / "config" / "ftbquests" / "quests"
    if not src.is_dir():
        src = root
    if not src.is_dir() or not (src / "data.snbt").exists():
        sys.exit(f"FTB quests dir not found under: {root}")

    data = parse_snbt(src / "data.snbt")
    pack_title = clean(data.get("title", "GregTech Community Pack"))

    # Chapter group ordering: index within chapter_groups.snbt.
    group_order, group_title = {}, {}
    groups = parse_snbt(src / "chapter_groups.snbt").get("chapter_groups", [])
    for idx, g in enumerate(groups):
        group_order[g["id"]] = idx
        group_title[g["id"]] = clean(g.get("title", ""))

    # --- chapters ---------------------------------------------------------
    chapters = []
    for path in sorted((src / "chapters").glob("*.snbt")):
        ch = parse_snbt(path)
        gid = ch.get("group", "")
        # FTB shows an ungrouped chapter in the implicit default group, BEFORE
        # the named groups -> -1. Named groups keep their declared order.
        gorder = -1 if not gid else group_order.get(gid, 999)
        chapters.append({
            "raw": ch,
            "key": ch.get("filename", path.stem),
            "title": clean(ch.get("title", path.stem)),
            "group": group_title.get(gid, ""),
            "_sort": (gorder, int(ch.get("order_index", 0))),
        })
    chapters.sort(key=lambda c: c["_sort"])

    # --- build output -----------------------------------------------------
    # Quests are global (each id appears once), chapters carry NODES that
    # reference quests with a per-chapter x/y. A node can come from a chapter's
    # own `quests:` array OR from its `quest_links:` array (FTB links display
    # quests defined elsewhere at a custom position) -- both yield identical
    # nodes here.
    out_chapters = []
    out_quests = []
    seen_quest_ids = set()
    task_types = Counter()
    item_ids = set()
    link_count = 0
    dropped_quests = []  # (qid, title, namespaces) for the report
    dropped_ids = set()

    for order, ch in enumerate(chapters):
        key = ch["key"]
        nodes = []

        for q in ch["raw"].get("quests", []):
            qid = q["id"]
            raw_tasks = q.get("tasks", []) or []

            # Skip quests referencing out-of-scope modded namespaces.
            icon_preview = quest_icon(q, raw_tasks)
            ns = quest_namespaces(icon_preview, raw_tasks)
            if ns & DROP_NAMESPACES:
                if qid not in dropped_ids:
                    dropped_ids.add(qid)
                    dropped_quests.append(
                        (qid, clean(q.get("title", "")) or "(untitled)", sorted(ns & DROP_NAMESPACES))
                    )
                continue

            # Node here, at this chapter's position.
            nodes.append({
                "quest": qid,
                "x": float(q.get("x", 0) or 0),
                "y": float(q.get("y", 0) or 0),
            })

            # Quest content registered once (its defining chapter).
            if qid in seen_quest_ids:
                continue
            seen_quest_ids.add(qid)

            tasks = []
            for t in raw_tasks:
                kind = t.get("type", "")
                if kind == "item":
                    task_types["item"] += 1
                    items, tag, label = task_items(t)
                    for i in items:
                        item_ids.add(i)
                    task = {"type": "item", "items": items, "count": task_count(t)}
                    if tag:
                        task["tag"] = tag
                    if label:
                        task["label"] = label
                    tasks.append(task)
                else:
                    task_types[kind or "(none)"] += 1
                    tasks.append({"type": "checkmark"})

            # A quest with no tasks auto-completes in FTB -> one checkmark here.
            if not tasks:
                tasks.append({"type": "checkmark"})

            # Title: FTB title, else derived from the first item task, else subtitle.
            title = clean(q.get("title", ""))
            if not title:
                first_item = ""
                for t in raw_tasks:
                    if t.get("type") == "item":
                        items, _, _ = task_items(t)
                        if items:
                            first_item = items[0]
                            break
                title = prettify(first_item) if first_item else clean(q.get("subtitle", "")) or "Quest"

            subtitle = clean(q.get("subtitle", ""))
            description = join_desc(q.get("description"))

            quest = {
                "id": qid,
                "icon": quest_icon(q, raw_tasks),
                "title": title,
                "deps": list(q.get("dependencies", []) or []),
                "tasks": tasks,
            }
            if subtitle:
                quest["subtitle"] = subtitle
            if description:
                quest["desc"] = description
            out_quests.append(quest)

        # quest_links: nodes only; the linked quest is defined in another chapter.
        for link in ch["raw"].get("quest_links", []) or []:
            qid = link.get("linked_quest")
            if not qid or qid in dropped_ids:
                continue
            nodes.append({
                "quest": qid,
                "x": float(link.get("x", 0) or 0),
                "y": float(link.get("y", 0) or 0),
            })
            link_count += 1

        out_chapters.append({
            "key": key,
            "order": order,
            "title": ch["title"],
            "group": ch["group"],
            "nodes": nodes,
        })

    # Strip dropped ids from downstream `deps` so the chain stays valid.
    for q in out_quests:
        if q["deps"]:
            q["deps"] = [d for d in q["deps"] if d not in dropped_ids]

    quest_count = len(out_quests)

    questlog = {
        "$generated": "tools/scripts/port-questbook.py -- do not hand-edit; re-run the script",
        "pack": pack_title,
        "chapters": out_chapters,
        "quests": out_quests,
    }

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    QUESTLOG.write_text(json.dumps(questlog, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    # --- report -----------------------------------------------------------
    gtceu = sum(1 for i in item_ids if i.startswith("gtceu:"))
    total_nodes = sum(len(c["nodes"]) for c in out_chapters)
    print(f"source     {src}")
    print(f"pack       {pack_title}")
    print(f"chapters   {len(out_chapters)}")
    print(f"quests     {quest_count}")
    print(f"nodes      {total_nodes}  ({link_count} from quest_links)")
    print(f"tasks      " + ", ".join(f"{k}={v}" for k, v in task_types.most_common()))
    print(f"distinct   {len(item_ids)} item ids ({gtceu} gtceu:, {len(item_ids) - gtceu} other)")
    if dropped_quests:
        print(f"dropped    {len(dropped_quests)} quests (out-of-scope mods: {sorted(DROP_NAMESPACES)})")
        for qid, title, mods in dropped_quests:
            print(f"             - {title}  [{','.join(mods)}]")
    print()
    print(f"wrote {QUESTLOG.relative_to(REPO)}")


if __name__ == "__main__":
    main()
