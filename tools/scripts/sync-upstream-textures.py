#!/usr/bin/env python3
"""Mirror upstream's texture tree byte-for-byte into Content/Textures/.

Wholesale wipe + copytree (idempotent), so deleted-upstream sprites also vanish
from the mirror. Every C# `gtceu:textures/<rel>.png` maps 1:1 to
`Content/Textures/<rel>.png`; derived art reads FROM the mirror, never edits it.
"""

import os
import shutil
import sys

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(REPO, "GregTech-Modern-1.20.1", "src", "main", "resources",
                   "assets", "gtceu", "textures")
DST = os.path.join(REPO, "GregTechCEuTerraria", "Content", "Textures")


def main():
    if not os.path.isdir(SRC):
        print(f"[error] upstream not found at {SRC}")
        return 1

    if os.path.isdir(DST):
        shutil.rmtree(DST)
    shutil.copytree(SRC, DST)

    n_png = sum(1 for _, _, fs in os.walk(DST) for f in fs if f.endswith(".png"))
    n_meta = sum(1 for _, _, fs in os.walk(DST) for f in fs if f.endswith(".mcmeta"))
    n_other = sum(1 for _, _, fs in os.walk(DST) for f in fs
                  if not f.endswith(".png") and not f.endswith(".mcmeta"))
    total_bytes = sum(os.path.getsize(os.path.join(r, f))
                      for r, _, fs in os.walk(DST) for f in fs)
    print(f"  {DST}")
    print(f"  {n_png} .png  {n_meta} .mcmeta  {n_other} other  "
          f"({total_bytes // 1024} KB total)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
