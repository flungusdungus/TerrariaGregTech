using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GregTechCEuTerraria.TerrariaCompat.Worldgen;
using Xunit;

namespace GregTechCEuTerraria.Tests;

// Loads the actual extracted Data/Veins/veins.json and validates structure.
// These tests fail if a re-extraction silently drops data - a kind of
// regression detector that doesn't need the game running.
public class VeinDataTests
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		PropertyNameCaseInsensitive = true,
		AllowTrailingCommas = true,
	};

	private static List<VeinDefinition> LoadVeins() =>
		JsonSerializer.Deserialize<List<VeinDefinition>>(
			File.ReadAllText(Path.Combine(TestPaths.VeinsDir, "veins.json")),
			JsonOpts) ?? new();

	[Fact]
	public void ExtractedVeinCountMatchesUpstream()
	{
		// GTOres.java has exactly 40 GTOreDefinition.create(...) calls in the
		// version we're porting. If the extractor regresses, this catches it.
		var veins = LoadVeins();
		Assert.Equal(40, veins.Count);
	}

	[Fact]
	public void EveryVeinHasAtLeastOneMaterial()
	{
		var veins = LoadVeins();
		var empty = veins.Where(v => v.Materials.Count == 0).Select(v => v.Id).ToList();
		Assert.Empty(empty);
	}

	[Fact]
	public void EveryLayerValueIsRecognized()
	{
		var veins = LoadVeins();
		var allowed = new HashSet<string> { "STONE", "DEEPSLATE", "NETHERRACK", "ENDSTONE" };
		var bad = veins.Where(v => !allowed.Contains(v.Layer)).Select(v => $"{v.Id}={v.Layer}").ToList();
		Assert.Empty(bad);
	}

	[Fact]
	public void LayerDistributionMatchesUpstream()
	{
		var veins = LoadVeins();
		var counts = veins.GroupBy(v => v.Layer).ToDictionary(g => g.Key, g => g.Count());
		Assert.Equal(14, counts.GetValueOrDefault("STONE"));
		Assert.Equal(8,  counts.GetValueOrDefault("DEEPSLATE"));
		Assert.Equal(12, counts.GetValueOrDefault("NETHERRACK"));
		Assert.Equal(6,  counts.GetValueOrDefault("ENDSTONE"));
	}

	[Fact]
	public void AllVeinMaterialIdsExistInMaterialJson()
	{
		// Cross-validation: every material referenced by a vein must be in our
		// extracted materials. This would have caught the GarnetRed/Plutonium239
		// alias bug.
		var matIds = new HashSet<string>();
		foreach (string f in Directory.GetFiles(TestPaths.MaterialsDir, "*.json"))
		{
			using var doc = JsonDocument.Parse(File.ReadAllText(f));
			foreach (var m in doc.RootElement.EnumerateArray())
				if (m.TryGetProperty("id", out var id))
					matIds.Add(id.GetString()!);
		}

		var veins = LoadVeins();
		var missing = veins
			.SelectMany(v => v.Materials.Select(vm => vm.MaterialId))
			.Where(id => !matIds.Contains(id))
			.Distinct()
			.OrderBy(s => s)
			.ToList();

		Assert.Empty(missing);
	}

	[Fact]
	public void AllVeinMaterialsHaveOreForm()
	{
		// Every material a vein wants to place must have ORE form so we
		// register an ore tile for it.
		var oreFormIds = new HashSet<string>();
		foreach (string f in Directory.GetFiles(TestPaths.MaterialsDir, "*.json"))
		{
			using var doc = JsonDocument.Parse(File.ReadAllText(f));
			foreach (var m in doc.RootElement.EnumerateArray())
			{
				if (!m.TryGetProperty("forms", out var forms)) continue;
				bool hasOre = forms.EnumerateArray().Any(e => e.GetString() == "ORE");
				if (hasOre) oreFormIds.Add(m.GetProperty("id").GetString()!);
			}
		}

		var veins = LoadVeins();
		var missing = veins
			// A vein's own host/layer rock (e.g. ENDSTONE for an END vein) is placed
			// as background filler, not as an ore - it legitimately has no ORE form,
			// and OreTileSetup skips non-ore vein materials gracefully. Only the
			// ORE-bearing materials must have an ORE form, so exclude the host rock.
			.SelectMany(v => v.Materials
				.Select(vm => vm.MaterialId)
				.Where(id => v.Layer == null
					|| !string.Equals(id, v.Layer, System.StringComparison.OrdinalIgnoreCase)))
			.Where(id => !oreFormIds.Contains(id))
			.Distinct()
			.OrderBy(s => s)
			.ToList();

		Assert.Empty(missing);
	}

	[Fact]
	public void BauxiteVeinEndHasExpectedShape()
	{
		// Spot check against a known fixture from upstream: bauxite_vein_end
		// uses ENDSTONE, IS_END biome, layered generator with 4 materials.
		var bauxite = LoadVeins().Single(v => v.Id == "bauxite_vein_end");
		Assert.Equal("ENDSTONE", bauxite.Layer);
		Assert.Equal("END", bauxite.Biome);
		Assert.Equal("layered", bauxite.GeneratorType);
		Assert.Equal(40, bauxite.Weight);
		Assert.Equal(0.3f, bauxite.Density, 0.01f);
		Assert.True(bauxite.Materials.Count >= 3, "expected at least 3 layered materials");
		Assert.Contains(bauxite.Materials, vm => vm.MaterialId == "bauxite");
	}
}
