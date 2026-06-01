#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Registry;

// Parses Data/Registry/items.json (produced by `./gradlew runData` +
// snapshot-registry.py) - THE single source of item identity for the mod.
// WireItem/MaterialItem/RegistryItem registries all consume these entries
// instead of synthesising ids, so registered ids/sets are byte-identical to
// upstream. Parsed at Mod.Load, before any item registry runs.
public static class RegistryDump
{
	private const string DataPath = "Data/Registry/items.json";

	// Upstream `ElectricStats`. A battery is exactly an entry with Dischargeable=true.
	public readonly record struct ElectricStats(long Capacity, int Tier, bool Chargeable, bool Dischargeable);

	// One composited render layer of a material item - texture path
	// + `Material.getLayerARGB` tint (-1 / 0xFFFFFFFF = untinted).
	public readonly record struct RenderLayer(string Texture, int Argb);

	// Field-by-field notes:
	// - Material/Prefix populated only for TagPrefixItem / MaterialBlockItem /
	//   MaterialPipeBlockItem entries; null otherwise.
	// - Electric populated only for ComponentItem with ElectricStats.
	// - RenderLayers populated for TagPrefixItem.
	// - Cover = bare cover-definition id (no `gtceu:`) for ComponentItem
	//   entries carrying a CoverPlaceBehavior.
	// - BlockTexture / ActiveBlockTexture for cube BlockItems (active set when
	//   the upstream blockstate has an `active=true` variant or `_bloom` overlay).
	public readonly record struct Entry(
		string Id, string BareId, string Class, string Name,
		int MaxStack, int Rarity, string? Material, string? Prefix, ElectricStats? Electric,
		IReadOnlyList<RenderLayer>? RenderLayers, string? Cover, string? BlockTexture,
		string? ActiveBlockTexture);

	private static readonly List<Entry> _entries = new();
	private static readonly Dictionary<string, Entry> _byBareId = new();

	// `gtceu:`-namespaced entries only; vanilla items resolve via VanillaSubstitution.
	public static IReadOnlyList<Entry> Entries => _entries;

	public static bool TryGet(string bareId, out Entry entry) =>
		_byBareId.TryGetValue(bareId, out entry);

	public static void Load(Mod mod)
	{
		_entries.Clear();
		_byBareId.Clear();

		using var stream = mod.GetFileStream(DataPath);
		if (stream is null)
		{
			mod.Logger.Warn($"Registry dump not found at {DataPath} - run " +
			                "`./gradlew runData` + tools/scripts/snapshot-registry.py.");
			return;
		}

		using var doc = JsonDocument.Parse(stream);
		foreach (var el in doc.RootElement.EnumerateArray())
		{
			string id = Str(el, "id");
			if (!id.StartsWith("gtceu:", StringComparison.Ordinal)) continue;

			string bareId = id["gtceu:".Length..];
			_entries.Add(new Entry(
				Id:       id,
				BareId:   bareId,
				Class:    Str(el, "class"),
				Name:     el.TryGetProperty("name", out var n) ? (n.GetString() ?? Humanize(bareId)) : Humanize(bareId),
				MaxStack: el.TryGetProperty("maxStack", out var ms) ? ms.GetInt32() : 64,
				Rarity:   MapRarity(el.TryGetProperty("rarity", out var r) ? (r.GetString() ?? "") : ""),
				Material: el.TryGetProperty("material", out var m) ? m.GetString() : null,
				Prefix:   el.TryGetProperty("prefix", out var p) ? p.GetString() : null,
				Electric: ParseElectric(el),
				RenderLayers: ParseRenderLayers(el),
				Cover:    ParseCover(el),
				BlockTexture: ParseBlockTexture(el),
				ActiveBlockTexture: ParseActiveBlockTexture(el)));
			_byBareId[bareId] = _entries[^1];
		}
		mod.Logger.Info($"RegistryDump: parsed {_entries.Count} gtceu item entries.");
	}

	private static IReadOnlyList<RenderLayer>? ParseRenderLayers(JsonElement el)
	{
		if (!el.TryGetProperty("render", out var r) ||
		    !r.TryGetProperty("layers", out var ls) || ls.ValueKind != JsonValueKind.Array)
			return null;
		var list = new List<RenderLayer>();
		foreach (var le in ls.EnumerateArray())
		{
			string tex = le.TryGetProperty("texture", out var t) ? (t.GetString() ?? "") : "";
			int argb = le.TryGetProperty("argb", out var a) ? a.GetInt32() : -1;
			if (tex.Length > 0) list.Add(new RenderLayer(tex, argb));
		}
		return list.Count > 0 ? list : null;
	}

	private static string? ParseBlockTexture(JsonElement el)
	{
		if (!el.TryGetProperty("render", out var r) ||
		    !r.TryGetProperty("texture", out var t)) return null;
		string tex = t.GetString() ?? "";
		return tex.Length > 0 ? tex : null;
	}

	private static string? ParseActiveBlockTexture(JsonElement el)
	{
		if (!el.TryGetProperty("render", out var r) ||
		    !r.TryGetProperty("activeTexture", out var t)) return null;
		string tex = t.GetString() ?? "";
		return tex.Length > 0 ? tex : null;
	}

	private static string? ParseCover(JsonElement el)
	{
		if (!el.TryGetProperty("cover", out var c)) return null;
		string raw = c.GetString() ?? "";
		if (raw.Length == 0) return null;
		return raw.StartsWith("gtceu:", StringComparison.Ordinal) ? raw["gtceu:".Length..] : raw;
	}

	private static ElectricStats? ParseElectric(JsonElement el) =>
		el.TryGetProperty("electricDischargeable", out var dis)
			? new ElectricStats(
				Capacity:      el.TryGetProperty("electricCapacity", out var c) ? c.GetInt64() : 0,
				Tier:          el.TryGetProperty("electricTier", out var t) ? t.GetInt32() : 0,
				Chargeable:    el.TryGetProperty("electricChargeable", out var chg) && chg.GetBoolean(),
				Dischargeable: dis.GetBoolean())
			: null;

	public static void Unload()
	{
		_entries.Clear();
		_byBareId.Clear();
	}

	private static string Str(JsonElement el, string prop) =>
		el.TryGetProperty(prop, out var v) ? (v.GetString() ?? "") : "";

	// MC Rarity -> closest Terraria ItemRarityID.
	public static int MapRarity(string rarity) => rarity switch
	{
		"UNCOMMON" => Terraria.ID.ItemRarityID.Yellow,
		"RARE"     => Terraria.ID.ItemRarityID.Cyan,
		"EPIC"     => Terraria.ID.ItemRarityID.LightPurple,
		_          => Terraria.ID.ItemRarityID.White,
	};

	// "silicon_boule" -> "Silicon Boule" - fallback when the dump has no `name`.
	public static string Humanize(string snake)
	{
		var parts = snake.Split('_');
		for (int i = 0; i < parts.Length; i++)
			if (parts[i].Length > 0) parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i][1..];
		return string.Join(' ', parts);
	}
}
