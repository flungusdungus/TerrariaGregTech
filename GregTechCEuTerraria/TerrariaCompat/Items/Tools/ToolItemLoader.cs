#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GregTechCEuTerraria.Api.Tool;
using GregTechCEuTerraria.Common.Materials;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Tools;

// One ToolItem per (material x GTToolType) - mirror of upstream
// GTMaterialItems.generateTools (walks every material with a TOOL property,
// emits a tool per ToolProperty.types[]). Source is materials.json's
// `tool.types`; ids via GTToolType.ResolveId, byte-identical to upstream.
public static class ToolItemLoader
{
	private const string ToolTexDir = "GregTechCEuTerraria/Content/Textures/item/tools/";

	// upstream id ("gtceu:iron_pickaxe") -> Terraria ItemType, for recipe refs.
	private static readonly Dictionary<string, int> _byUpstreamId = new();
	public static IReadOnlyDictionary<string, int> ByUpstreamId => _byUpstreamId;

	public static bool TryGet(string upstreamId, out int itemType) =>
		_byUpstreamId.TryGetValue(upstreamId, out itemType);

	// Crafting-catalyst tag -> tool item types. Held-not-consumed crafting tools;
	// built here (the one place that has both items and their GTToolType).
	private static readonly (GTToolType Base, string Tag)[] _catalystClasses =
	{
		(GTToolType.HARD_HAMMER, "gtceu:tools/crafting_hammers"),
		(GTToolType.SOFT_MALLET, "gtceu:tools/crafting_mallets"),
		(GTToolType.KNIFE,       "gtceu:tools/crafting_knives"),
		(GTToolType.FILE,        "gtceu:tools/crafting_files"),
		(GTToolType.SAW,         "gtceu:tools/crafting_saws"),
		(GTToolType.WRENCH,      "gtceu:tools/crafting_wrenches"),
		(GTToolType.SCREWDRIVER, "gtceu:tools/crafting_screwdrivers"),
		(GTToolType.WIRE_CUTTER, "gtceu:tools/crafting_wire_cutters"),
		(GTToolType.MORTAR,      "gtceu:tools/crafting_mortars"),
		(GTToolType.CROWBAR,     "gtceu:tools/crafting_crowbars"),
	};

	// Aliases overlapping the gtceu tags, plus the non-catalyst tools
	// (mining_hammer). Same lookup path as the catalyst tags.
	private static readonly (GTToolType Base, string Tag)[] _forgeToolTags =
	{
		(GTToolType.HARD_HAMMER,   "forge:tools/hammers"),
		(GTToolType.SOFT_MALLET,   "forge:tools/mallets"),
		(GTToolType.KNIFE,         "forge:tools/knives"),
		(GTToolType.FILE,          "forge:tools/files"),
		(GTToolType.SAW,           "forge:tools/saws"),
		(GTToolType.WRENCH,        "forge:tools/wrenches"),
		(GTToolType.SCREWDRIVER,   "forge:tools/screwdrivers"),
		(GTToolType.WIRE_CUTTER,   "forge:tools/wire_cutters"),
		(GTToolType.MORTAR,        "forge:tools/mortars"),
		(GTToolType.MINING_HAMMER, "forge:tools/mining_hammers"),
	};

	private static readonly Dictionary<string, List<int>> _craftingTagItems = new();
	public static IReadOnlyDictionary<string, List<int>> CraftingTagItems => _craftingTagItems;

	// Every crafting-catalyst tool item type (union of CraftingTagItems) - the
	// recipe bridge's consume-callback zeroes consumption for these.
	public static readonly HashSet<int> CatalystItemTypes = new();

	public static void Register(Mod mod)
	{
		_byUpstreamId.Clear();
		_craftingTagItems.Clear();
		CatalystItemTypes.Clear();

		var bundled = mod.GetFileNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
		bool TexExists(string stem) => bundled.Contains($"Content/Textures/item/tools/{stem}.rawimg");

		int registered = 0, skipped = 0;
		foreach (var (_, material) in MaterialRegistry.All)
		{
			if (!material.HasTool()) continue;

			Color primary = RGB(material.Color);
			Color secondary = material.SecondaryColor is { } sc ? RGB(sc) : primary;

			foreach (var typeName in material.Tool!.Types)
			{
				var type = GTToolType.Get(typeName);
				if (type == null) continue; // type not in the registry

				string id = type.ResolveId(material.Id);
				if (mod.TryFind<ModItem>(id, out _)) continue;
				if (!ToolModel.Layers.TryGetValue(type.Name, out var stems)) { skipped++; continue; }

				var layers = BuildLayers(stems, primary, secondary, TexExists, out string? headTex);
				if (layers == null) { skipped++; continue; }

				var item = new ToolItem(id, TitleCase(id), type, material, layers, headTex!);
				mod.AddContent(item);
				_byUpstreamId[$"gtceu:{id}"] = item.Type;
				registered++;

				// Catalyst when type's ToolClasses contains the base (folds
				// electric wrenches/buzzsaws into wrench/saw).
				foreach (var (baseType, tag) in _catalystClasses)
				{
					if (!type.ToolClasses.Contains(baseType)) continue;
					if (!_craftingTagItems.TryGetValue(tag, out var list))
						_craftingTagItems[tag] = list = new List<int>();
					list.Add(item.Type);
					CatalystItemTypes.Add(item.Type);
				}

				// Additional forge:tools/* tag aliases - widen lookup only.
				foreach (var (baseType, tag) in _forgeToolTags)
				{
					if (!type.ToolClasses.Contains(baseType)) continue;
					if (!_craftingTagItems.TryGetValue(tag, out var list))
						_craftingTagItems[tag] = list = new List<int>();
					list.Add(item.Type);
				}
			}
		}

		mod.Logger.Info($"ToolItemLoader: registered {registered} tools" +
			(skipped > 0 ? $" ({skipped} skipped - missing model/texture)" : "") + ".");
	}

	// Marks itemType as a catalyst for every tag - a Gregith counts as every
	// tool simultaneously (used by GregithItemLoader).
	public static void RegisterAsCatalystForAllTags(int itemType)
	{
		foreach (var (_, tag) in _catalystClasses)
		{
			if (!_craftingTagItems.TryGetValue(tag, out var list))
				_craftingTagItems[tag] = list = new List<int>();
			if (!list.Contains(itemType)) list.Add(itemType);
		}
		foreach (var (_, tag) in _forgeToolTags)
		{
			if (!_craftingTagItems.TryGetValue(tag, out var list))
				_craftingTagItems[tag] = list = new List<int>();
			if (!list.Contains(itemType)) list.Add(itemType);
		}
		CatalystItemTypes.Add(itemType);
	}

	public static void Unload()
	{
		_byUpstreamId.Clear();
		_craftingTagItems.Clear();
		CatalystItemTypes.Clear();
	}

	// Verbatim IGTTool.tintColor: layer 1 = material, layer 2 = secondary,
	// others untinted. "void" layers skip but keep their slot (later layers
	// retain their original tint index).
	private static ToolLayer[]? BuildLayers(string[] stems, Color primary, Color secondary,
		Func<string, bool> texExists, out string? headTex)
	{
		headTex = null;
		var layers = new List<ToolLayer>(stems.Length);
		for (int i = 0; i < stems.Length; i++)
		{
			string stem = stems[i];
			if (stem == ToolModel.Void) continue;
			if (!texExists(stem)) return null; // a required layer is missing

			string path = ToolTexDir + stem;
			Color tint = i switch { 1 => primary, 2 => secondary, _ => Color.White };
			layers.Add(new ToolLayer(path, tint));
			if (i == 1) headTex = path;
		}
		if (layers.Count == 0) return null;
		headTex ??= layers[0].TexturePath;
		return layers.ToArray();
	}

	private static Color RGB(uint? c) =>
		c is { } v ? new Color((byte)(v >> 16), (byte)(v >> 8), (byte)v) : Color.White;

	private static string TitleCase(string id) =>
		CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('_', ' '));
}
