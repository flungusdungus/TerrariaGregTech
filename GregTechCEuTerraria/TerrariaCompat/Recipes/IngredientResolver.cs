#nullable enable
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.Api.Fluids;
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Items;

namespace GregTechCEuTerraria.TerrariaCompat.Recipes;

// IIngredientResolver - consumed by IngredientJson / GTRecipeSerializer when
// materializing runData JSON. Delegates to existing static surfaces
// (VanillaItemMap, MaterialItemRegistry, RegistryItemLoader, FluidRegistry).
public sealed class IngredientResolverImpl : IIngredientResolver
{
	public static readonly IngredientResolverImpl Instance = new();

	public int ResolveItemType(string upstreamId)
	{
		if (string.IsNullOrEmpty(upstreamId)) return 0;

		// 1. Hand-curated mappings.
		if (VanillaItemMap.TryGet(upstreamId, out var v)) return v;

		// 2. Inert GT items from the registry dump + fluid cells / tools / armor.
		if (Items.Registry.RegistryItemLoader.TryGet(upstreamId, out var reg)) return reg;
		if (Items.Tools.ToolItemLoader.TryGet(upstreamId, out var tool)) return tool;
		if (Items.Armor.ArmorItemLoader.TryGet(upstreamId, out var armor)) return armor;

		// Programmed circuit - dedicated IntCircuitBehaviour port.
		if (upstreamId == "gtceu:programmed_circuit")
			return Terraria.ModLoader.ModContent.ItemType<IntCircuitItem>();
		if (TryStripGtceuPrefix(upstreamId, out var bare) &&
		    Items.Fluids.FluidCellRegistry.TryGet(bare, out var cell))
			return cell;

		// 3. Dynamic material x prefix items.
		if (MaterialItemRegistry.TryGetByUpstreamId(upstreamId, out var matItem))
			return matItem;

		// 4. Last resort - hand-registered ModItems whose Name is the bare id
		//    (e.g. boss-summon items). Only gtceu:* ids reach here.
		if (TryStripGtceuPrefix(upstreamId, out var modBare) &&
		    Terraria.ModLoader.ModContent.TryFind<Terraria.ModLoader.ModItem>("GregTechCEuTerraria", modBare, out var custom))
			return custom.Type;

		return 0;
	}

	public IReadOnlyList<int> ResolveItemTag(string tagName)
	{
		if (string.IsNullOrEmpty(tagName)) return Array.Empty<int>();

		// Crafting-catalyst tool tags (h/f/w/... resolved by ToolItemLoader).
		if (Items.Tools.ToolItemLoader.CraftingTagItems.TryGetValue(tagName, out var catalystItems))
			return catalystItems;

		var types = new List<int>();

		// Vanilla tag substitution first so the Terraria item lands even when
		// the upstream tag dump also lists gtceu:* members (e.g. rubber_log).
		if (VanillaItemMap.TryGetTagItem(tagName, out var vt))
			types.Add(vt);
		// EXCLUSIVE multi-item mappings (forge:marble -> only Marble Block,
		// minecraft:fishes -> 8 species) - short-circuit the dump expansion
		// below so dump gtceu:* members can't slip in.
		bool exclusive = false;
		if (VanillaItemMap.TryGetTagItems(tagName, out var multi))
		{
			foreach (var t in multi)
				if (t > 0 && !types.Contains(t)) types.Add(t);
			exclusive = true;
		}

		// GT item tags - expand recursively + resolve each member. Skipped
		// when MultiTagItems already declared the full match set.
		if (!exclusive && Items.Registry.RegistryTagLoader.HasTag(tagName))
		{
			foreach (var memberId in Items.Registry.RegistryTagLoader.ExpandItems(tagName))
			{
				int t = ResolveItemType(memberId);
				if (t > 0 && !types.Contains(t)) types.Add(t);
			}
		}

		// MaterialItemRegistry - forge:ingots/iron, c:dusts/copper, etc.
		if (types.Count == 0 && MaterialItemRegistry.TryGetByTagPath(tagName, out var matItem))
			types.Add(matItem);

		return types.Count > 0 ? types : (IReadOnlyList<int>)Array.Empty<int>();
	}

	public FluidType? ResolveFluidType(string upstreamId)
	{
		if (string.IsNullOrEmpty(upstreamId)) return null;
		// FluidRegistry keys are bare - strip the namespace.
		int colon = upstreamId.IndexOf(':');
		string id = colon >= 0 ? upstreamId[(colon + 1)..] : upstreamId;
		return Api.Fluids.FluidRegistry.TryGet(id, out var f) ? f : null;
	}

	public IReadOnlyList<FluidType> ResolveFluidTag(string tagName)
	{
		// Naked-id fallback (forge:fluids/water -> water -> FluidRegistry entry).
		// Real tag-set semantics deferred until a recipe needs multi-fluid tags.
		var t = ResolveFluidType(tagName);
		return t is null ? Array.Empty<FluidType>() : new[] { t };
	}

	private static bool TryStripGtceuPrefix(string upstreamId, out string bareId)
	{
		const string prefix = "gtceu:";
		if (upstreamId.StartsWith(prefix, StringComparison.Ordinal))
		{
			bareId = upstreamId[prefix.Length..];
			return true;
		}
		bareId = upstreamId;
		return false;
	}
}
