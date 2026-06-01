#!/usr/bin/env python3
"""Dump the file table of a .tmod for a sanity check of packaged assets.

Usage: inspect-tmod.py [path/to/mod.tmod]
Defaults to the current user's standard tML Mods folder (Windows / Linux / macOS).
"""
import struct, io, sys, os
from pathlib import Path

def default_tmod_path() -> Path:
    """Locate the built .tmod in the standard per-user tML Mods folder."""
    mod_name = "GregTechCEuTerraria.tmod"
    home = Path.home()
    candidates = [
        home / "Documents" / "My Games" / "Terraria" / "tModLoader" / "Mods" / mod_name,           # Windows
        home / ".local" / "share" / "Terraria" / "tModLoader" / "Mods" / mod_name,                 # Linux
        home / "Library" / "Application Support" / "Terraria" / "tModLoader" / "Mods" / mod_name,  # macOS
    ]
    for c in candidates:
        if c.exists():
            return c
    sys.exit(
        "Could not locate the built .tmod. Pass its path as argv[1].\n"
        f"Searched:\n  " + "\n  ".join(str(c) for c in candidates)
    )

p = Path(sys.argv[1]) if len(sys.argv) > 1 else default_tmod_path()

with open(p, "rb") as f:
    data = f.read()

def read7bit(buf):
    n, s = 0, 0
    while True:
        b = buf.read(1)[0]
        n |= (b & 0x7F) << s
        if (b & 0x80) == 0:
            return n
        s += 7

def rstr(buf):
    ln = read7bit(buf)
    return buf.read(ln).decode("utf-8")

bio = io.BytesIO(data)
assert bio.read(4) == b"TMOD"
tmlver = rstr(bio)
_hash = bio.read(20)
_sig = bio.read(256)
dlen = struct.unpack("<I", bio.read(4))[0]
payload = bio.read(dlen)
pb = io.BytesIO(payload)
name = rstr(pb)
ver = rstr(pb)
count = struct.unpack("<i", pb.read(4))[0]
print(f"mod={name} ver={ver} tmlver={tmlver} files={count} sizeKiB={len(data)//1024}")

files = []
for _ in range(count):
    fn = rstr(pb)
    orig = struct.unpack("<i", pb.read(4))[0]
    comp = struct.unpack("<i", pb.read(4))[0]
    files.append((fn, orig, comp))

exts = {}
for fn, o, c in files:
    e = fn.rsplit(".", 1)[-1].lower() if "." in fn else "(none)"
    exts.setdefault(e, []).append(fn)
print("\nExtensions:")
for e in sorted(exts, key=lambda k: -len(exts[k])):
    print(f"  .{e}: {len(exts[e])}")

tops = {}
for fn, o, c in files:
    top = fn.split("/")[0] if "/" in fn else fn
    tops[top] = tops.get(top, 0) + 1
print("\nTop-level entries:")
for t, n in sorted(tops.items(), key=lambda kv: -kv[1]):
    print(f"  {t}: {n}")

# Scan for vanilla Re-Logic art that shouldn't be in a 3rd-party mod. Vanilla
# names are PascalCase + underscore-index (Tiles_38, NPC_Head_1, Items_1, ...).
import re
VANILLA_RE = re.compile(
    r"(?:^|/)(Tiles|Items|NPC|NPC_Head|Wall|Background|Projectile|Gore|Buff|"
    r"Extra|Liquid|Town|Map_)_?\d+\.(rawimg|png|xnb)$"
)
suspects = [fn for fn, o, c in files if VANILLA_RE.search(fn)]
print(f"\nHeuristic vanilla-asset suspects (filename contains vanilla prefixes): {len(suspects)}")
for fn in suspects[:50]:
    print(f"  {fn}")
if len(suspects) > 50:
    print(f"  ... +{len(suspects)-50} more")
