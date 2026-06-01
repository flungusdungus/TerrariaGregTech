#nullable enable
using System;
using System.Collections.Generic;
// System used by Func<,,> delegate; keep the import.
using GregTechCEuTerraria.Api.Recipe.Ingredient.Nbtpredicate;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Recipe.Ingredient;

// LOCKED - port of
// com.gregtechceu.gtceu.api.recipe.ingredient.NBTPredicateIngredient.
//
// Ingredient that matches a specific Terraria item type AND has matching
// NBT according to a predicate. Used for recipes that need an item with
// specific NBT contents (e.g. battery-buffer recipes that need a charged
// battery, fluid-cell recipes that need a specific fluid loaded).
//
// Documented adaptations:
//   - Upstream stores an `ItemStack stack` example + `NBTPredicate` predicate.
//     We store the resolved Terraria item type (int) + predicate.
//   - CompoundTag -> TagCompound for NBT comparison.
//   - The default `ALWAYS_TRUE` predicate matches any NBT (degenerates to
//     ItemStackIngredient behavior).
public sealed class NBTPredicateIngredient : Ingredient
{
	public static readonly NBTPredicate ALWAYS_TRUE = TrueNBTPredicate.INSTANCE;

	// NBT-aware item-resolver hook - TerrariaCompat installs a callback that
	// resolves `(itemId, nbtPayload) -> ItemType`. Called by `IngredientJson.
	// ReadNBTPredicate` when an `nbt` field is present, so a recipe ingredient
	// referencing a canonical id whose per-stack variants are registered as
	// distinct ItemIDs (today: `gtceu:turbine_rotor` -> per-material
	// `<material>_turbine_rotor` via `TurbineRotorItemLoader`) lands on the
	// right Terraria ItemID at parse time. Returns 0 when it doesn't know how
	// to resolve the (id, nbt) pair - caller falls back to whatever the
	// regular resolver returned.
	public static Func<string, string, int>? ResolveItemTypeFromNbt;

	public int ItemType { get; }
	public NBTPredicate Predicate { get; }
	public string UpstreamId { get; }

	// Raw SNBT payload from the recipe JSON's `nbt` field (e.g.
	// `"{GT.PartStats:{Material:\"gtceu:aluminium\"}}"`). Kept for diagnostics
	// + future input-side NBT matching; for OUTPUT-side per-material item
	// resolution, the right ItemID is already baked into `ItemType` at parse
	// time via `ResolveItemTypeFromNbt`. Null = no NBT carried on this ingredient.
	public string? OutputNbt { get; }

	// Lazy - see GetItems().
	private IReadOnlyList<Item>? _exampleList;

	public NBTPredicateIngredient(int itemType, NBTPredicate predicate, string upstreamId = "", string? outputNbt = null)
	{
		ItemType = itemType;
		Predicate = predicate;
		UpstreamId = upstreamId;
		OutputNbt = outputNbt;
	}

	public static NBTPredicateIngredient Of(int itemType, NBTPredicate predicate, string upstreamId = "", string? outputNbt = null) =>
		new(itemType, predicate, upstreamId, outputNbt);

	public static NBTPredicateIngredient Of(int itemType, string upstreamId = "") =>
		Of(itemType, ALWAYS_TRUE, upstreamId);

	public override bool Test(Item item)
	{
		if (item is null) return false;
		if (item.type != ItemType) return false;
		// Terraria items carry NBT via ModItem.SaveData -> TagCompound.
		// For runtime NBT comparison we need the live tag - ModItem instances
		// own their state but Item.ModItem is per-stack via tML's clone path.
		// Upstream's `input.getOrCreateTag()` lands as: if item has a
		// GlobalItem-or-ModItem-backed TagCompound, read it. For now we pass
		// null (= "no NBT"); the predicate's Test(null) decides the match.
		// When per-item NBT serialization is wired into the recipe-match
		// path, replace null with the live tag.
		return Predicate.Test(null);
	}

	// Lazy-built - see ItemStackIngredient.GetItems for why the example stack
	// can't be created until after tML resizes the modded item arrays.
	//
	// `ItemType` is already the right per-stack variant for NBT-keyed item
	// families (e.g. the per-material `aluminium_turbine_rotor`) because
	// `IngredientJson.ReadNBTPredicate` resolves the `nbt` payload via
	// `ResolveItemTypeFromNbt` at parse time.
	public override IReadOnlyList<Item> GetItems()
	{
		if (_exampleList is null)
		{
			var ex = new Item();
			ex.SetDefaults(ItemType);
			_exampleList = new[] { ex };
		}
		return _exampleList;
	}

	public override bool IsEmpty => ItemType == 0;

	public override string GetTypeName() => "forge:nbt";

	public override string ToString() => $"NBTPredicate({(UpstreamId.Length > 0 ? UpstreamId : ItemType.ToString())}, {Predicate.GetTypeName()})";
}
