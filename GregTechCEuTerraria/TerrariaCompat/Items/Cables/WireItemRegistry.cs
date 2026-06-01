#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Common.Materials;
using GregTechCEuTerraria.TerrariaCompat.Items.Registry;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Cables;

// Registers one WireItem per MaterialPipeBlockItem dump entry with a
// `wireGt*` (uninsulated) or `cableGt*` (insulated) prefix. Name = upstream id
// verbatim. MaterialItemRegistry skips these so they aren't double-registered.
public static class WireItemRegistry
{
	// Key = (materialId, wireSize, insulated)
	private static readonly Dictionary<(string, byte, bool), WireItem> _items = new();

	public static int Count => _items.Count;

	public static int? Get(string materialId, byte wireSize, bool insulated) =>
		_items.TryGetValue((materialId, wireSize, insulated), out var it) ? it.Type : null;

	private const string PipeItemClass = "com.gregtechceu.gtceu.api.item.MaterialPipeBlockItem";

	// Upstream pipe TagPrefix -> wire-size byte. Kind derived from prefix
	// (StartsWith "cable") at registration time.
	private static readonly Dictionary<string, byte> PrefixToSize = new()
	{
		["wireGtSingle"]     = 1,
		["wireGtDouble"]     = 2,
		["wireGtQuadruple"]  = 4,
		["wireGtOctal"]      = 8,
		["wireGtHex"]        = 16,
		["cableGtSingle"]    = 1,
		["cableGtDouble"]    = 2,
		["cableGtQuadruple"] = 4,
		["cableGtOctal"]     = 8,
		["cableGtHex"]       = 16,
	};

	public static void Register(Mod mod)
	{
		_items.Clear();

		int missingMaterial = 0, missingCableTier = 0;
		foreach (var e in RegistryDump.Entries)
		{
			if (e.Class != PipeItemClass) continue;
			if (e.Prefix is null || !PrefixToSize.TryGetValue(e.Prefix, out byte size)) continue;
			if (e.Material is null) { missingMaterial++; continue; }

			var material = MaterialRegistry.Get(e.Material);
			if (material is null) { missingMaterial++; continue; }
			// Missing CableTier = extractor gap; dump is authoritative, register
			// anyway (BuildCell falls back to ULV). Log so the gap is visible.
			if (material.CableTier is null) missingCableTier++;

			bool insulated = e.Prefix.StartsWith("cable", StringComparison.Ordinal);
			var item = new WireItem(e.BareId, material, size, insulated);
			mod.AddContent(item);
			_items[(material.Id, size, insulated)] = item;
		}

		mod.Logger.Info($"WireItemRegistry: registered {_items.Count} wire + cable items from the registry dump" +
			(missingMaterial > 0 ? $" ({missingMaterial} skipped - material not in MaterialRegistry)" : "") +
			(missingCableTier > 0 ? $" ({missingCableTier} have no CableTier - extractor gap, ULV fallback)" : "") + ".");
	}

	public static void Unload() => _items.Clear();
}
