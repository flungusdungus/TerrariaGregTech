#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Registry;

// Binds dump entries to C# by Java class. Two families:
//  - net.minecraft.world.item.Item - inert GT items.
//  - ComponentItem - ORPHANS only (not batteries, not covers; those are owned
//    by Battery/CoverItemLoader running before/after).
// Material x prefix items belong to MaterialItem/WireItem registries.
public static class RegistryItemLoader
{
	private const string PlainItemClass     = "net.minecraft.world.item.Item";
	private const string ComponentItemClass = "com.gregtechceu.gtceu.api.item.ComponentItem";

	// upstream id (e.g. "gtceu:resistor") -> Terraria ItemType.
	private static readonly Dictionary<string, int> _byUpstreamId = new();
	public static IReadOnlyDictionary<string, int> ByUpstreamId => _byUpstreamId;

	public static bool TryGet(string upstreamId, out int itemType) =>
		_byUpstreamId.TryGetValue(upstreamId, out itemType);

	public static void Load(Mod mod)
	{
		_byUpstreamId.Clear();

		// Absent PNG would crash autoload - probe each ComponentItem first.
		var bundled = mod.GetFileNames().ToHashSet(StringComparer.OrdinalIgnoreCase);

		int registered = 0;
		foreach (var e in RegistryDump.Entries)
		{
			// Defer to dedicated registries / vanilla substitution. Also avoids
			// tML's "duplicate ModItem name" load failure.
			if (mod.TryFind<ModItem>(e.BareId, out _)) continue;
			if (Recipes.VanillaItemMap.TryGet(e.Id, out _)) continue;

			// Replaced by per-material `<material>_turbine_rotor` family
			// (TurbineRotorItemLoader); recipes resolve via the NBT hook on
			// `NBTPredicateIngredient.ResolveItemTypeFromNbt`.
			if (e.BareId == "turbine_rotor") continue;

			RegistryItem? item = e.Class switch
			{
				PlainItemClass     => new RegistryItem(e.BareId, e.Name, e.MaxStack, e.Rarity),
				ComponentItemClass => BuildComponentItem(e, bundled),
				_                  => null,   // class not ported here
			};
			if (item is null) continue;

			mod.AddContent(item);
			_byUpstreamId[e.Id] = item.Type;
			registered++;
		}

		mod.Logger.Info($"RegistryItemLoader: registered {registered} items.");
	}

	// Returns null (skip) for cover items and textureless orphans.
	private static RegistryItem? BuildComponentItem(RegistryDump.Entry e, HashSet<string> bundled)
	{
		if (e.Cover != null) return null;          // owned by CoverItemLoader

		string? texOverride = null;
		if (e.BareId.EndsWith("_power_unit", StringComparison.Ordinal))
		{
			// No standalone item sprite - reuse the tool model's power-unit tex.
			string tier = e.BareId[..^"_power_unit".Length];
			string rel = $"Content/Textures/item/tools/power_unit_{tier}";
			if (!bundled.Contains(rel + ".rawimg")) return null;
			texOverride = "GregTechCEuTerraria/" + rel;
		}
		else if (!bundled.Contains($"Content/Textures/item/{e.BareId}.rawimg"))
		{
			return null;
		}

		return new RegistryItem(e.BareId, e.Name, e.MaxStack, e.Rarity, texOverride);
	}

	public static void Unload() => _byUpstreamId.Clear();
}
