# GregTechCEu - Terraria Port

> So if terraria is 2d minecraft, we can just remove Z coordinate from code and expect our minecraft mod to work in terraria, right?...
>
> ..right?...
>
> ....Oh, btw, this mod has an electric Fisher which simulates fishing, so you can finally avoid interacting with terraria fishing completely~

A port of [GregTech Modern (GTCEu)](https://github.com/GregTechCEu/GregTech-Modern) to Terraria 1.4.4. 

> Note: This is an unofficial port. Do not request support in original mod

Discord: https://discord.gg/sqs4G7u6eX

## Why this exists?

- Several years ago I thought "are there actually any tech mods for terraria?" - and it turned out those are almost completely non-existent (hope Macrocosm releases soon....)
- Several months ago I got interested in reading the source code of modern gregtech for self-education purposes, as it's almost the biggest mod for minecraft out there. But just reading code is dumb and unproductive
- Several days ago I had that exact thought, "what if I port the whole of gregtech to terraria instead of doing literally anything actually useful?", and then immediately agreed with myself (it was 5 AM btw)

## Features

### Changes in GTCEu logic

- Wires have their own layer (similar to terraria wire layer) because I thought it's more convenient that way. Machines are connected from behind (specific connection rules on transformers, see item tooltips)
- Wires of different energy tiers won't connect (just a UX change, you're expected to use transformers anyway)
- Added solar panels and lamps
- Pollution disabled (but I'm really thinking of implementing it, so it'll spread corruption/crimson...)
- Maintenance disabled (objectively useless feature)

### Integration from Gregtech to Terraria

- Resources:
  - GT ore generation (ore drop rates boosted, as terraria world is pretty small)
  - Large Miner and Large Fluid Drilling things are mining depending on a biome for easy infinite resources
  - Fisher simulates terraria fishing
- Recipes:
  - GT smelting recipes can be done in terraria furnace (gated by energy tier)
  - MC crafting can be done in terraria workbench
  - Easier rubber-related recipes for easier early-game
- Utility
  - Block breaker mines a tunnel below itself
  - Singleblock Miner and Pump mine tiles below themselves in the caves (width/depth scale with tier)
  - Steam Age Skip Bag - because we all know steam age gets old real fast
  - Bosses drop age-relevant raw resources
  - Bosses drop age-relevant whole-multiblock bags with a 1% chance (only low-skill greggers gotta open the bags, I guess?)
  - Gregith (zenith but gregified)

### Integration from Terraria to Gregtech

- Terraria recipes can be done in gt machines
  - Furnaces Furnace ULV
  - Hellforge Furnace MV
  - AdamantiteForge Furnace EV
  - LihzahrdFurnace Furnace LuV
  - GlassKiln Arc Furnace LV
  - BoneWelder Forging Press LV
  - HoneyDispenser into Canner MV
  - DemonAltar into Circuit Assembler LV

### Boss drops

#### Pre-Hardmode

Steam Bronze Invar

- King Slime - raw metals for getting through steam age

LV Steel Tin Copper

- Eye Of Cthulhu - steel, LV circuit components
- Eater of Worlds/Brain of Cthulhu - steel, LV circuit components
- Deerclops - raw metals that are useful in LV

MV Aluminium Copper Cupronickel

- Queen Bee - raw metals that are useful in MV
- Skeletron - raw metals that are useful in MV, MV circuit components
- Wall Of Flesh - raw metals that are useful in MV, MV circuit components

#### Hardmode

HV Stainless Steel Silver Electrum

- Flying Dutchman - raw metals that are useful in HV
- The Destroyer - raw metals that are useful in HV, HV circuit components
- The Twins - raw metals that are useful in HV, HV circuit components
- Skeletron Prime - raw metals that are useful in HV, HV circuit components

EV Titanium Aluminium Kanthal

- Queen Slime - raw metals that are useful in EV
- Plantera - raw metals that are useful in EV, EV circuit components

#### Post-Plantera

IV Tungsten Steel Tungsten Graphene

- Mourning Wood - raw metals that are useful in IV
- Everscream - raw metals that are useful in IV
- Pumpking - raw metals that are useful in IV, IV circuit components
- Santa-NK1 - raw metals that are useful in IV, IV circuit components
- Ice Queen - raw metals that are useful in IV, IV circuit components

LuV HSS-S Niobium-Titanium Ruridit

- Golem - raw metals that are useful in LuV

#### Post-golem

- Martian Saucer - raw metals that are useful in LuV, LuV circuit components

ZPM Osmiridium Vanadium-Gallium Europium

- Duke Fishron - raw metals that are useful in ZPM
- Lunatic Cultist - raw metals that are useful in ZPM, ZPM circuit components

UV Tritanium Yttrium-Barium-Cuprate Americium

- Pillars - raw metals that are useful in UV
- Empress Of Light - raw metals that are useful in UV, UV circuit components
- Moon Lord - raw metals that are useful in UV, UV circuit components

### TODO

- gregify vanilla recipes (optional mod config flag) so vanilla progression is nearly impossible without progressing gregtech - e.g. all weapons are crafted specifically in gt machines
- stargate ? moonlord gate ?
- Post Moon Lord Macrocosm compatibility

## Developing

### Structure

- `GregTech-Modern-1.20.1/` - GT source, not committed
- `GregTechCEuTerraria/` - mod sources
- `tests` - unit tests ported from GT, not covering a whole lot
- `tools/scripts/` - Python scripts that snapshot upstream's `./gradlew runData` registry dumps + recipes + textures into `Data/` and `Content/`

### Building

- Junction `GregTechCEuTerraria/` into `%USERPROFILE%\Documents\My Games\Terraria\tModLoader\ModSources\GregTechCEuTerraria` (mklink /J "%USERPROFILE%\Documents\My Games\Terraria\tModLoader\ModSources\GregTechCEuTerraria" "D:\NotWork\TerrariaGregTech\GregTechCEuTerraria")
- Build in tModLoader

## AI Usage

Some people asked, yes, Claude was used to speed up the porting process, of course. Theres no fun in manually rewriting stuff line by line, and the existing AST-based java->c# porters are too dumb for such a complex thing. So be aware there still will be stupid unexpected game breaking bugs, play at your own caution

## License

LGPL-3.0-or-later - inherited from GTCEu. See [COPYING.LESSER](COPYING.LESSER) and [COPYING](COPYING).
