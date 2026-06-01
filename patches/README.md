# Upstream patches

Patches we apply to a fresh checkout of [GregTech Modern](https://github.com/GregTechCEu/GregTech-Modern) so its data-generator emits the extra dumps (`items.json`, `materials.json`, `veins.json`, recipes-with-dedup, etc.) that this port consumes.

The upstream source itself is gitignored (`GregTech-Modern-1.20.1/`) — we only commit the patches.

## What's in `upstream-datagen.patch`

Modifies two files in the upstream tree:

- `src/main/java/com/gregtechceu/gtceu/data/DataGenerators.java` — adds the registry-dump / item-tag / ore-vein / material `DataProvider`s, a `RecipeProvider` dedup-by-`ResourceLocation` wrapper, plus the `extras` synthetic-item list. Verbatim with what CLAUDE.md's `tools/scripts/` section describes.
- `src/main/java/com/gregtechceu/gtceu/GTCEu.java` — adds the `GTCEu.isClientThread` null-guard so datagen runs headless without a Minecraft client context.

**Base**: branch `1.20.1` @ commit `934cca815` ("Built-in connected texture implementation (#4791)") of <https://github.com/GregTechCEu/GregTech-Modern>.

The commit hash also appears at the top of the patch file. When upstream evolves past this commit, the patch may still apply (most of upstream's churn is in unrelated files); if it doesn't, rebase onto the new head and regenerate (see below).

## How to set up upstream from scratch

```bash
# from repo root
git clone --branch 1.20.1 https://github.com/GregTechCEu/GregTech-Modern.git GregTech-Modern-1.20.1
cd GregTech-Modern-1.20.1
patch -p1 < ../patches/upstream-datagen.patch
./gradlew runData            # produces src/generated/resources/...
```

After `runData` finishes, run the snapshot/sync scripts (`tools/scripts/snapshot-registry.py`, `snapshot-recipes.py`, `sync-upstream-textures.py`, `sync-upstream-sounds.py`) to regenerate everything `Data/` and `Content/` consumes.

## Regenerating the patch

If you ever edit the upstream source directly (to extend datagen), regenerate the patch against a pristine clone of the same base commit:

```bash
# at repo root
git clone --depth 1 --branch 1.20.1 https://github.com/GregTechCEu/GregTech-Modern.git /tmp/gtm-pristine
diff -u /tmp/gtm-pristine/src/main/java/com/gregtechceu/gtceu/data/DataGenerators.java \
        GregTech-Modern-1.20.1/src/main/java/com/gregtechceu/gtceu/data/DataGenerators.java \
        > patches/upstream-datagen.patch
diff -u /tmp/gtm-pristine/src/main/java/com/gregtechceu/gtceu/GTCEu.java \
        GregTech-Modern-1.20.1/src/main/java/com/gregtechceu/gtceu/GTCEu.java \
        >> patches/upstream-datagen.patch
```

Then commit the new patch.
