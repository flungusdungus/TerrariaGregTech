#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Registry;

// Loads tags.json + fluid_tags.json (runData -> snapshot-registry.py). Members
// are concrete ids or `#`-prefixed nested-tag refs; ExpandItems/ExpandFluids
// flatten recursively.
//
// NB: material-prefix tags (forge:ingots/iron, ...) aren't in these dumps
// (attached at registration, not via datagen); TagMembership reconstructs them
// from MaterialItemRegistry.
public static class RegistryTagLoader
{
	private const string ItemDataPath = "Data/Registry/tags.json";
	private const string FluidDataPath = "Data/Registry/fluid_tags.json";

	private static readonly Dictionary<string, List<string>> _itemTags = new();
	private static readonly Dictionary<string, List<string>> _fluidTags = new();

	public static bool HasTag(string tagId) => _itemTags.ContainsKey(tagId);
	public static bool HasFluidTag(string tagId) => _fluidTags.ContainsKey(tagId);

	public static IReadOnlyCollection<string> AllItemTags => _itemTags.Keys;
	public static IReadOnlyCollection<string> AllFluidTags => _fluidTags.Keys;

	public static void Load(Mod mod)
	{
		_itemTags.Clear();
		_fluidTags.Clear();
		LoadFile(mod, ItemDataPath, _itemTags, "item");
		LoadFile(mod, FluidDataPath, _fluidTags, "fluid");
		mod.Logger.Info($"RegistryTagLoader: loaded {_itemTags.Count} item tags, " +
		                $"{_fluidTags.Count} fluid tags.");
	}

	private static void LoadFile(Mod mod, string path, Dictionary<string, List<string>> into, string kind)
	{
		using var stream = mod.GetFileStream(path);
		if (stream is null)
		{
			mod.Logger.Warn($"{kind} tag dump not found at {path} - run " +
			                "`./gradlew runData` + tools/scripts/snapshot-registry.py.");
			return;
		}

		using var doc = JsonDocument.Parse(stream);
		foreach (var prop in doc.RootElement.EnumerateObject())
		{
			var members = new List<string>();
			foreach (var v in prop.Value.EnumerateArray())
				if (v.GetString() is { } s) members.Add(s);
			into[prop.Name] = members;
		}
	}

	public static void Unload()
	{
		_itemTags.Clear();
		_fluidTags.Clear();
	}

	public static IReadOnlyList<string> ExpandItems(string tagId)
	{
		var result = new List<string>();
		Walk(tagId, result, new HashSet<string>(), _itemTags);
		return result;
	}

	public static IReadOnlyList<string> ExpandFluids(string tagId)
	{
		var result = new List<string>();
		Walk(tagId, result, new HashSet<string>(), _fluidTags);
		return result;
	}

	private static void Walk(string tagId, List<string> outIds, HashSet<string> seen,
		Dictionary<string, List<string>> tags)
	{
		if (!seen.Add(tagId)) return;   // cycle guard
		if (!tags.TryGetValue(tagId, out var members)) return;
		foreach (var m in members)
		{
			if (m.StartsWith("#", StringComparison.Ordinal))
				Walk(m[1..], outIds, seen, tags);
			else if (!outIds.Contains(m))
				outIds.Add(m);
		}
	}
}
