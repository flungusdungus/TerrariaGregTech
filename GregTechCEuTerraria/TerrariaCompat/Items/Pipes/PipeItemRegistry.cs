#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Common.Materials;
using GregTechCEuTerraria.TerrariaCompat.Items.Registry;
using GregTechCEuTerraria.TerrariaCompat.Pipelike;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Pipes;

// One PipeItem per MaterialPipeBlockItem dump entry with a `pipe*` prefix.
// Name = upstream id verbatim. Wire/cable prefixes are owned by WireItemRegistry.
public static class PipeItemRegistry
{
	private const string PipeItemClass = "com.gregtechceu.gtceu.api.item.MaterialPipeBlockItem";

	// Key = upstream bare id.
	private static readonly Dictionary<string, PipeItem> _items = new();

	public static int Count => _items.Count;

	public static int? Get(string bareId) =>
		_items.TryGetValue(bareId, out var it) ? it.Type : null;

	// Restrictive is a flavour of Item pipe; rides as a bool so the layer enum
	// stays a pure {Item, Fluid} discriminator.
	private static readonly Dictionary<string, (string Size, PipeKind Layer, bool Restrictive)> PrefixToInfo = new()
	{
		["pipeTinyFluid"]         = ("tiny",      PipeKind.Fluid, false),
		["pipeSmallFluid"]        = ("small",     PipeKind.Fluid, false),
		["pipeNormalFluid"]       = ("normal",    PipeKind.Fluid, false),
		["pipeLargeFluid"]        = ("large",     PipeKind.Fluid, false),
		["pipeHugeFluid"]         = ("huge",      PipeKind.Fluid, false),
		["pipeQuadrupleFluid"]    = ("quadruple", PipeKind.Fluid, false),
		["pipeNonupleFluid"]      = ("nonuple",   PipeKind.Fluid, false),
		["pipeSmallItem"]         = ("small",     PipeKind.Item,  false),
		["pipeNormalItem"]        = ("normal",    PipeKind.Item,  false),
		["pipeLargeItem"]         = ("large",     PipeKind.Item,  false),
		["pipeHugeItem"]          = ("huge",      PipeKind.Item,  false),
		["pipeSmallRestrictive"]  = ("small",     PipeKind.Item,  true),
		["pipeNormalRestrictive"] = ("normal",    PipeKind.Item,  true),
		["pipeLargeRestrictive"]  = ("large",     PipeKind.Item,  true),
		["pipeHugeRestrictive"]   = ("huge",      PipeKind.Item,  true),
	};

	public static void Register(Mod mod)
	{
		_items.Clear();

		int missingMaterial = 0;
		foreach (var e in RegistryDump.Entries)
		{
			if (e.Class != PipeItemClass) continue;
			if (e.Prefix is null || !PrefixToInfo.TryGetValue(e.Prefix, out var info)) continue;
			if (e.Material is null) { missingMaterial++; continue; }

			var material = MaterialRegistry.Get(e.Material);
			if (material is null) { missingMaterial++; continue; }

			var item = new PipeItem(e.BareId, e.Name, material, info.Size, info.Layer, info.Restrictive);
			mod.AddContent(item);
			_items[e.BareId] = item;
		}

		mod.Logger.Info($"PipeItemRegistry: registered {_items.Count} pipe items from the registry dump" +
			(missingMaterial > 0 ? $" ({missingMaterial} skipped - material not in MaterialRegistry)" : "") + ".");
	}

	public static void Unload()
	{
		_items.Clear();
		ItemIconBaker.ClearCache();
	}
}
