#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GregTechCEuTerraria.Common.Materials;
using GregTechCEuTerraria.Api.Data.Chemical.Material;
using GregTechCEuTerraria.TerrariaCompat.Items.Registry;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items;

// One MaterialItem per upstream-registered material x prefix item, enumerated
// from the registry dump. Set + ids are byte-identical to upstream - replaces
// the old material x prefix synthesis that produced phantoms (e.g. gtceu:copper_ingot).
//
// Classes consumed here: TagPrefixItem (ingot/dust/plate/rod/gem/...) and
// MaterialBlockItem (storage / raw-ore / frame blocks). MaterialPipeBlockItem
// is WireItemRegistry's; inert plain items are RegistryItemLoader's.
public static class MaterialItemRegistry
{
	private const string TagPrefixItemClass    = "com.gregtechceu.gtceu.api.item.TagPrefixItem";
	private const string MaterialBlockItemClass = "com.gregtechceu.gtceu.api.item.MaterialBlockItem";

	private static readonly Dictionary<(string materialId, string prefixId), MaterialItem> _items = new();
	// Secondary id patterns only - primary `gtceu:<id>` resolves via Mod.Find
	// (Name == upstream id). e.g. RawOre item id is `raw_%s` but recipes also
	// reference the ore-block id `%s_ore`.
	private static readonly Dictionary<string, int> _byUpstreamId = new(StringComparer.Ordinal);
	private static readonly Dictionary<string, int> _byTagPath = new(StringComparer.Ordinal);

	public static int Count => _items.Count;

	public static int? Get(string materialId, string prefixId) =>
		_items.TryGetValue((materialId, prefixId), out var item) ? item.Type : null;

	public static bool TryGetByUpstreamId(string id, out int itemType)
	{
		string stripped = StripNamespace(id);
		var mod = ModLoader.GetMod("GregTechCEuTerraria");
		if (mod.TryFind<ModItem>(stripped, out var item)) { itemType = item.Type; return true; }
		return _byUpstreamId.TryGetValue(stripped, out itemType);
	}

	public static bool TryGetByTagPath(string tag, out int itemType)
	{
		string stripped = StripNamespace(tag);
		return _byTagPath.TryGetValue(stripped, out itemType);
	}

	public static void RegisterTagAlias(string tagPath, int itemType) =>
		_byTagPath[StripNamespace(tagPath)] = itemType;

	private static string StripNamespace(string id)
	{
		int colon = id.IndexOf(':');
		return colon >= 0 ? id.Substring(colon + 1) : id;
	}

	public static IEnumerable<(string MaterialId, string PrefixId, MaterialItem Item)> All =>
		_items.Select(p => (p.Key.materialId, p.Key.prefixId, p.Value));

	public static void Register(Mod mod)
	{
		_items.Clear();
		_byUpstreamId.Clear();
		_byTagPath.Clear();

		var prefixByUpstream = BuildPrefixMap();
		int unmappedPrefix = 0, missingMaterial = 0, noRender = 0;

		// Humanized-name fallbacks; port-locale.py emits the real entries.
		foreach (var material in MaterialRegistry.All.Values)
			RegisterDisplayName(mod, $"Mods.GregTechCEuTerraria.Materials.{material.Id}", () => Humanize(material.Id));

		foreach (var e in RegistryDump.Entries)
		{
			var prefix = ResolvePrefix(e, prefixByUpstream);
			if (prefix is null)
			{
				// A TagPrefixItem with no mapped MaterialPrefix = real coverage gap.
				if (e.Class == TagPrefixItemClass) unmappedPrefix++;
				continue;
			}
			if (e.Material is null) { missingMaterial++; continue; }
			// Sculk: closed-loop dead-end material, intentionally not ported
			// (recipes dropped at snapshot-recipes extraction).
			if (e.Material == "sculk") continue;
			var material = MaterialRegistry.Get(e.Material);
			if (material is null) { missingMaterial++; continue; }

			var layers = e.RenderLayers ?? (IReadOnlyList<RegistryDump.RenderLayer>)Array.Empty<RegistryDump.RenderLayer>();
			if (layers.Count == 0) noRender++;

			var item = new MaterialItem(e.BareId, material, prefix, layers);

			// Humanized DisplayName fallback; port-locale.py emits the real ones.
			string materialDisplayKey = $"Mods.GregTechCEuTerraria.Materials.{material.Id}";
			string itemDisplayKey = $"Mods.GregTechCEuTerraria.Items.{item.Name}.DisplayName";
			string template = prefix.DisplayTemplate;
			RegisterDisplayName(mod, itemDisplayKey, () => string.Format(template, Language.GetTextValue(materialDisplayKey)));

			mod.AddContent(item);
			_items[(material.Id, prefix.Id)] = item;

			if (prefix.IdPatterns != null)
				foreach (var pat in prefix.IdPatterns)
				{
					string aliasId = pat.Replace("%s", material.Id);
					if (aliasId != e.BareId) _byUpstreamId[aliasId] = item.Type;
				}
			if (prefix.TagPaths != null)
				foreach (var pat in prefix.TagPaths)
					_byTagPath[pat.Replace("%s", material.Id)] = item.Type;
		}

		mod.Logger.Info($"Registered {_items.Count} material items from the registry dump " +
			$"(reverse maps: {_byUpstreamId.Count} ids, {_byTagPath.Count} tags)." +
			(noRender > 0 ? $" ({noRender} have no render layers - vanilla-texture fallback)" : "") +
			(unmappedPrefix > 0 ? $" ({unmappedPrefix} TagPrefixItems skipped - prefix not ported)" : "") +
			(missingMaterial > 0 ? $" ({missingMaterial} skipped - material not in MaterialRegistry)" : ""));
	}

	public static void Unload()
	{
		_items.Clear();
		_byUpstreamId.Clear();
		_byTagPath.Clear();
	}

	// Returns null when this entry isn't a material item we own.
	private static MaterialPrefix? ResolvePrefix(RegistryDump.Entry e, IReadOnlyDictionary<string, MaterialPrefix> map)
	{
		if (e.Prefix is null) return null;
		return e.Class switch
		{
			TagPrefixItemClass    => map.GetValueOrDefault(e.Prefix),
			// Storage / raw-ore / frame blocks only; ore-host stones aren't ours.
			MaterialBlockItemClass when e.Prefix == "block" => MaterialPrefixes.Block,
			MaterialBlockItemClass when e.Prefix == "rawOreBlock" => MaterialPrefixes.RawOreBlock,
			MaterialBlockItemClass when e.Prefix == "frame" => MaterialPrefixes.Frame,
			_ => null,
		};
	}

	private static IReadOnlyDictionary<string, MaterialPrefix> BuildPrefixMap()
	{
		var map = new Dictionary<string, MaterialPrefix>(StringComparer.Ordinal);
		foreach (var p in MaterialPrefixes.All)
			map[UpstreamPrefixName(p)] = p;
		return map;
	}

	// Almost always snake_case -> camelCase (small_dust -> smallDust); these are
	// the few that don't follow that rule.
	private static readonly Dictionary<string, string> PrefixNameOverrides = new(StringComparer.Ordinal)
	{
		["raw_ore"]          = "raw",
		["crushed"]          = "crushedOre",
		["crushed_purified"] = "purifiedOre",
		["crushed_refined"]  = "refinedOre",
	};

	private static string UpstreamPrefixName(MaterialPrefix p) =>
		PrefixNameOverrides.TryGetValue(p.Id, out var n) ? n : SnakeToCamel(p.Id);

	private static string SnakeToCamel(string snake)
	{
		var parts = snake.Split('_');
		var sb = new StringBuilder(parts[0]);
		for (int i = 1; i < parts.Length; i++)
			if (parts[i].Length > 0)
				sb.Append(char.ToUpperInvariant(parts[i][0])).Append(parts[i][1..]);
		return sb.ToString();
	}

	private static void RegisterDisplayName(Mod mod, string key, Func<string> fallback) =>
		Language.GetOrRegister(key, fallback);

	// "annealed_copper" -> "Annealed Copper"
	private static string Humanize(string snake)
	{
		var sb = new StringBuilder(snake.Length);
		bool capNext = true;
		foreach (char c in snake)
		{
			if (c == '_') { sb.Append(' '); capNext = true; continue; }
			sb.Append(capNext ? char.ToUpperInvariant(c) : c);
			capNext = false;
		}
		return sb.ToString();
	}
}
