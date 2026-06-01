#nullable enable
using System.Collections.Generic;

namespace GregTechCEuTerraria.TerrariaCompat.Worldgen;

// Mirrors the extractor's VeinDefinitionDto. Loaded from Data/Veins/veins.json
// at Mod.Load and consumed by OreWorldGen during ModifyWorldGenTasks.
//
// MC concepts mapped at consumption time:
//   Layer  : STONE | DEEPSLATE | NETHERRACK | ENDSTONE  -> Terraria depth band
//   Biome  : OVERWORLD | NETHER | END                  -> informational only (band is set by Layer)
//   HeightMin/Max: Minecraft Y (low=bedrock, high=sky) -> preserved for future-use refinement
public sealed class VeinDefinition
{
	public required string Id { get; init; }
	public string Layer { get; init; } = "STONE";
	public string Biome { get; init; } = "OVERWORLD";
	public int HeightMin { get; init; }
	public int HeightMax { get; init; }
	public int Weight { get; init; } = 10;
	public float Density { get; init; } = 0.5f;
	public int ClusterSizeMin { get; init; } = 8;
	public int ClusterSizeMax { get; init; } = 16;
	public string GeneratorType { get; init; } = "layered";
	public List<VeinMaterial> Materials { get; init; } = new();
}

public sealed record VeinMaterial(string MaterialId, int Weight, int SizeMin, int SizeMax);
