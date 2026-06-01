#!/usr/bin/env python3
"""Verbatim byte-for-byte mirror of upstream's `assets/gtceu/sounds/` into
`Content/Sounds/`. Wiped + re-populated each run (removed upstream files vanish).

  python tools/scripts/sync-upstream-sounds.py [--input <dir>] [--output <dir>]
"""
from __future__ import annotations

import argparse
import shutil
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_INPUT  = REPO_ROOT / "GregTech-Modern-1.20.1" / "src" / "main" / "resources" / "assets" / "gtceu" / "sounds"
DEFAULT_OUTPUT = REPO_ROOT / "GregTechCEuTerraria" / "Content" / "Sounds"


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--input",  type=Path, default=DEFAULT_INPUT)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()

    if not args.input.is_dir():
        raise SystemExit(f"Input directory not found: {args.input}")

    if args.output.exists():
        shutil.rmtree(args.output)
    args.output.mkdir(parents=True)

    count = 0
    for src in sorted(args.input.rglob("*.ogg")):
        rel = src.relative_to(args.input)
        dst = args.output / rel
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dst)
        count += 1

    print(f"Mirrored {count} OGG files from {args.input.relative_to(REPO_ROOT)} -> {args.output.relative_to(REPO_ROOT)}")


if __name__ == "__main__":
    main()
