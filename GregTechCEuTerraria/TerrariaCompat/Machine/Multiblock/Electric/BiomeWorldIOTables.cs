#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.TerrariaCompat.Items;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Electric;

// Biome -> drop tables for world-I/O multis. Deliberate divergence from
// upstream's chunk-radius ore mining / per-chunk fluid veins (neither works
// in 2D): substitute biome-keyed lottery. LargeMiner picks one material per
// cycle from a weighted pool; FluidDrillingRig fills with base_mB x rigMult.
// Mappings locked with the user before implementation.
public static class BiomeWorldIOTables
{
	// Relative weights only; pool total is not required to be 100.
	public readonly record struct OreDrop(string MaterialId, int Weight);

	public static readonly Dictionary<BiomeProbe.Biome, OreDrop[]> Ores = new()
	{
		[BiomeProbe.Biome.Forest] = new[]
		{
			new OreDrop("copper",   40), new OreDrop("tin",     40),
			new OreDrop("iron",     20), new OreDrop("gold",    10),
			new OreDrop("apatite",   5),
		},
		[BiomeProbe.Biome.Desert] = new[]
		{
			new OreDrop("aluminium", 35), new OreDrop("iron",     30),
			new OreDrop("bauxite",   25), new OreDrop("gold",     15),
			new OreDrop("tungsten",   8),
		},
		[BiomeProbe.Biome.Snow] = new[]
		{
			new OreDrop("silver",   40), new OreDrop("lead",     35),
			new OreDrop("iron",     20), new OreDrop("nickel",   15),
			new OreDrop("platinum", 10),
		},
		[BiomeProbe.Biome.Jungle] = new[]
		{
			new OreDrop("copper",    30), new OreDrop("gold",     25),
			new OreDrop("platinum",  20), new OreDrop("palladium",10),
			new OreDrop("iridium",   10), new OreDrop("emerald",   5),
		},
		[BiomeProbe.Biome.Ocean] = new[]
		{
			new OreDrop("nickel",    40), new OreDrop("cobalt",   35),
			new OreDrop("salt",      20), new OreDrop("rock_salt",15),
			new OreDrop("magnesium", 10),
		},
		[BiomeProbe.Biome.Mushroom] = new[]
		{
			new OreDrop("bauxite",   35), new OreDrop("magnesium",30),
			new OreDrop("neodymium", 20), new OreDrop("iridium",  15),
			new OreDrop("titanium",  10),
		},
		[BiomeProbe.Biome.Crimson] = new[]
		{
			new OreDrop("pyrite",    30), new OreDrop("sphalerite",30),
			new OreDrop("zinc",      25), new OreDrop("redstone",  20),
			new OreDrop("chromite",  10),
		},
		[BiomeProbe.Biome.Corruption] = new[]
		{
			new OreDrop("tetrahedrite",30), new OreDrop("galena",   30),
			new OreDrop("sulfur",       25), new OreDrop("redstone", 20),
			new OreDrop("molybdenum",   10),
		},
		[BiomeProbe.Biome.Hallow] = new[]
		{
			new OreDrop("magnesium", 30), new OreDrop("apatite",   25),
			new OreDrop("beryllium", 20), new OreDrop("diamond",   15),
			new OreDrop("sapphire",   8), new OreDrop("ruby",       8),
		},
		[BiomeProbe.Biome.Underworld] = new[]
		{
			new OreDrop("pyrolusite",30), new OreDrop("magnetite", 30),
			new OreDrop("pyrite",    25), new OreDrop("saltpeter", 15),
			new OreDrop("lithium",   10), new OreDrop("naquadah",  10),
		},
	};

	// Materials where upstream defers raw form to minecraft:raw_<material>
	// (the dump has only the gtceu:raw_<material>_block storage block).
	// Pinning to vanilla ore keeps the smelting chain in player progression.
	private static readonly Dictionary<string, int> _vanillaOreFallback = new()
	{
		["iron"]     = ItemID.IronOre,
		["copper"]   = ItemID.CopperOre,
		["gold"]     = ItemID.GoldOre,
		["tin"]      = ItemID.TinOre,
		["silver"]   = ItemID.SilverOre,
		["lead"]     = ItemID.LeadOre,
		["platinum"] = ItemID.PlatinumOre,
		["tungsten"] = ItemID.TungstenOre,
		["titanium"] = ItemID.TitaniumOre,
		["cobalt"]   = ItemID.CobaltOre,
	};

	// Resolution: raw_ore -> vanilla fallback -> gem -> dust. Single source of
	// truth for both RollOre and the recipe-browser synth.
	public static int ResolveOreItem(string materialId)
	{
		int? gtRaw = MaterialItemRegistry.Get(materialId, "raw_ore");
		if (gtRaw is not null) return gtRaw.Value;
		if (_vanillaOreFallback.TryGetValue(materialId, out var vanilla)) return vanilla;
		int? gem = MaterialItemRegistry.Get(materialId, "gem");
		if (gem is not null) return gem.Value;
		int? dust = MaterialItemRegistry.Get(materialId, "dust");
		return dust ?? 0;
	}

	public static (int ItemType, string MaterialId) RollOre(BiomeProbe.Biome biome, System.Random rng)
	{
		if (!Ores.TryGetValue(biome, out var pool) || pool.Length == 0) return (0, "");

		int totalWeight = 0;
		foreach (var entry in pool) totalWeight += entry.Weight;
		int roll = rng.Next(totalWeight);

		int accum = 0;
		foreach (var entry in pool)
		{
			accum += entry.Weight;
			if (roll >= accum) continue;
			return (ResolveOreItem(entry.MaterialId), entry.MaterialId);
		}
		return (0, "");
	}

	// fluid_drilling_rig - mirrors user's locked mapping (upstream GTBedrockFluids
	// deposit set). Hallow->oil substitutes raw_oil (not in dump). Default->oil.
	private static readonly Dictionary<BiomeProbe.Biome, string> _fluidMaterialIds = new()
	{
		[BiomeProbe.Biome.Forest]     = "oil_light",
		[BiomeProbe.Biome.Desert]     = "oil",
		[BiomeProbe.Biome.Snow]       = "natural_gas",
		[BiomeProbe.Biome.Jungle]     = "natural_gas",
		[BiomeProbe.Biome.Ocean]      = "salt_water",
		[BiomeProbe.Biome.Mushroom]   = "natural_gas",
		[BiomeProbe.Biome.Crimson]    = "lava",
		[BiomeProbe.Biome.Corruption] = "oil_heavy",
		[BiomeProbe.Biome.Hallow]     = "oil",
		[BiomeProbe.Biome.Underworld] = "lava",
		// Cavern / fallback -> "oil" via the GetFluid switch below.
	};

	public static FluidType? GetFluid(BiomeProbe.Biome biome)
	{
		if (biome == BiomeProbe.Biome.Underworld) return FluidRegistry.Lava;
		if (biome == BiomeProbe.Biome.Crimson)    return FluidRegistry.Lava;
		var id = _fluidMaterialIds.TryGetValue(biome, out var matId) ? matId : "oil";
		return FluidRegistry.Get(id);
	}
}
