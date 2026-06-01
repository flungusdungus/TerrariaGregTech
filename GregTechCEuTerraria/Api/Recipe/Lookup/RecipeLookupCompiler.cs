#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine.Feature;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
// The base ingredient TYPE is `...Recipe.Ingredient.Ingredient`, whose leaf
// name collides with the `...Recipe.Ingredient` NAMESPACE as seen from inside
// `Api.Recipe.*`. Alias it so the bare name resolves to the type.
using GtIngredient = GregTechCEuTerraria.Api.Recipe.Ingredient.Ingredient;

namespace GregTechCEuTerraria.Api.Recipe.Lookup;

// ADAPTED - stands in for upstream's
// com.gregtechceu.gtceu.api.recipe.lookup.StagingRecipeDB (the recipe->trie
// decomposition) + MapIngredientTypeManager (the per-capability dispatch).
//
// Two jobs:
//   BUILD  - TryCompileRecipe: a GTRecipe's input + tick-input item/fluid
//            ingredients -> List<List<AbstractMapIngredient>> for RecipeDB.Add.
//   QUERY  - CompileQuery: a machine's available input items/fluids/circuit ->
//            List<List<AbstractMapIngredient>> for the RecipeDB iterator.
//
// Documented adaptations vs upstream:
//   - MapIngredientTypeManager is a reflective, class-hierarchy-walking
//     registry for an open set of ingredient/capability types. We have a
//     fixed, closed set (item + fluid capabilities; ItemStack / Tag / Sized /
//     IntProvider / IntCircuit ingredients), so the dispatch is a direct
//     switch - no reflection, no registry.
//   - The frequency sort in StagingRecipeDB.populateDB (rarest ingredient
//     first, for trie compactness) is NOT ported - it's a pure optimisation;
//     skipping it only makes the trie slightly less shared, never incorrect.
//   - EU capability is excluded from the trie (upstream: isRecipeSearchFilter
//     == false for EU).
//   - An ingredient with no type-level key (NBT-predicate, or an unresolved
//     ItemStack/Tag) makes TryCompileRecipe return null - the caller
//     (GTRecipeType.EnsureDb) routes that recipe to the always-scanned
//     fallback list, so it is never lost.
public static class RecipeLookupCompiler
{
	// === BUILD ==============================================================

	// Decompose a recipe's searchable inputs into the per-slot ingredient-key
	// lists RecipeDB.Add expects. Returns null when any ingredient cannot be
	// represented as a trie key - the caller must then flat-scan that recipe.
	public static List<List<AbstractMapIngredient>>? TryCompileRecipe(GTRecipe recipe)
	{
		var slots = new List<List<AbstractMapIngredient>>();
		if (!AddItemSlots(recipe.GetInputContents(ItemRecipeCapability.CAP), slots))       return null;
		if (!AddItemSlots(recipe.GetTickInputContents(ItemRecipeCapability.CAP), slots))   return null;
		if (!AddFluidSlots(recipe.GetInputContents(FluidRecipeCapability.CAP), slots))     return null;
		if (!AddFluidSlots(recipe.GetTickInputContents(FluidRecipeCapability.CAP), slots)) return null;
		return slots;
	}

	private static bool AddItemSlots(
		IReadOnlyList<Content.Content> contents, List<List<AbstractMapIngredient>> slots)
	{
		foreach (var c in contents)
		{
			if (c.Payload is not GtIngredient ing) return false;
			var keys = DecomposeItem(ing);
			if (keys == null) return false;
			slots.Add(keys);
		}
		return true;
	}

	private static bool AddFluidSlots(
		IReadOnlyList<Content.Content> contents, List<List<AbstractMapIngredient>> slots)
	{
		foreach (var c in contents)
		{
			if (c.Payload is not FluidIngredient fi) return false;
			var keys = DecomposeFluid(fi);
			if (keys == null) return false;
			slots.Add(keys);
		}
		return true;
	}

	// One item ingredient -> its alternative trie keys, or null when it has no
	// type-level key (the recipe then goes to the flat-scan fallback).
	private static List<AbstractMapIngredient>? DecomposeItem(GtIngredient ing)
	{
		// Unwrap the count wrappers down to the matching ingredient.
		while (true)
		{
			if (ing is SizedIngredient s)       { ing = s.Inner; continue; }
			if (ing is IntProviderIngredient p) { ing = p.Inner; continue; }
			break;
		}
		switch (ing)
		{
			case IntCircuitIngredient circuit:
				return new List<AbstractMapIngredient> { new CircuitMapIngredient(circuit.Configuration) };
			case ItemStackIngredient item:
				return item.ItemType != 0
					? new List<AbstractMapIngredient> { new ItemMapIngredient(item.ItemType) }
					: null;
			case TagIngredient tag:
			{
				// A tag pre-resolves to a flat type list - expand to one key
				// per resolved type (see ItemMapIngredient's header).
				var keys = new List<AbstractMapIngredient>(tag.ResolvedTypes.Count);
				foreach (int type in tag.ResolvedTypes)
					if (type != 0) keys.Add(new ItemMapIngredient(type));
				return keys.Count > 0 ? keys : null;
			}
			default:
				// NBTPredicateIngredient / any future keyless ingredient.
				return null;
		}
	}

	// One fluid ingredient -> its alternative trie keys, or null when it
	// resolves to no fluids.
	private static List<AbstractMapIngredient>? DecomposeFluid(FluidIngredient fi)
	{
		var fluids = fi.GetFluids();
		if (fluids.Count == 0) return null;
		var keys = new List<AbstractMapIngredient>(fluids.Count);
		foreach (var f in fluids) keys.Add(new FluidMapIngredient(f.Id));
		return keys;
	}

	// === QUERY ==============================================================

	// Decompose a machine's currently-available recipe-search inputs into the
	// per-input ingredient-key lists the RecipeDB iterator walks with. Each
	// distinct available item / fluid is one slot; the machine's circuit
	// selector is always contributed as a final slot.
	public static List<List<AbstractMapIngredient>> CompileQuery(IRecipeLogicMachine holder)
	{
		var query = new List<List<AbstractMapIngredient>>();

		var seenItems = new HashSet<int>();
		var seenCircuits = new HashSet<int>();
		foreach (var item in holder.LookupInputItems)
		{
			if (item is null || item.IsAir) continue;
			// IntCircuitItem maps to CircuitMapIngredient (distinguished by
			// Configuration), mirroring how DecomposeItem emits CircuitMapIngredient
			// for an IntCircuitIngredient on the build side. Don't double-emit
			// an ItemMapIngredient - the build side doesn't.
			if (item.ModItem is TerrariaCompat.Items.IntCircuitItem circuit)
			{
				if (seenCircuits.Add(circuit.Configuration))
					query.Add(new List<AbstractMapIngredient> { new CircuitMapIngredient(circuit.Configuration) });
				continue;
			}
			if (seenItems.Add(item.type))
				query.Add(new List<AbstractMapIngredient> { new ItemMapIngredient(item.type) });
		}

		var seenFluids = new HashSet<string>();
		foreach (var fluid in holder.LookupInputFluids)
		{
			if (fluid.IsEmpty || fluid.Type is null) continue;
			if (seenFluids.Add(fluid.Type.Id))
				query.Add(new List<AbstractMapIngredient> { new FluidMapIngredient(fluid.Type.Id) });
		}

		return query;
	}
}
