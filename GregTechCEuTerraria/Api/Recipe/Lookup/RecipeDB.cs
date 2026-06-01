#nullable enable
using System;
using System.Collections.Generic;

namespace GregTechCEuTerraria.Api.Recipe.Lookup;

// PARTIAL - port of com.gregtechceu.gtceu.api.recipe.lookup.RecipeDB.
//
// The recipe-lookup trie: recipes indexed by their input ingredients so a
// machine search only walks recipes whose inputs are a subset of what's
// available, instead of the flat per-station linear scan.
//
// THIS PHASE ports the trie core verbatim - Add / AddRecursive /
// NodesForIngredient / Find(list, predicate) / RecipeIterator / Clear.
//
// NOT yet ported (Phase 2 - needs the capability->AbstractMapIngredient
// conversion):
//   - fromHolder(IRecipeCapabilityHolder)        - machine inputs -> ingredient lists
//   - find(holder) / find(Map) / iterator(holder) - the holder-driven entrypoints
//   - the COMBUSTION_GENERATOR_FUELS -> PowerlessJetpack.FUELS side effect in add()
//     (a combustion-fuel registration hack; not applicable until generators
//     route through the DB)
//   - recipe.recipeCategory.addRecipe(recipe) on a successful add (category
//     bookkeeping - our GTRecipeCategory has no per-DB recipe set yet)
//
// Documented adaptations:
//   - com.mojang.datafixers.util.Either -> the minimal Either<TL,TR> in this
//     namespace.
//   - Map.compute(key, remappingFunction) has no C# equivalent; AddRecursive
//     ports the remapping inline (the upstream remapper never returns null
//     here, so it always reduces to a TryGetValue + write-back).
//   - upstream logs a recipe-conflict warning in the compute remapper; omitted
//     - an Api type has no logger handle. The conflict BEHAVIOUR is identical:
//     the incumbent recipe is kept.
public sealed class RecipeDB
{
	private readonly Branch _rootBranch = new();

	// Clear the DB.
	public void Clear() => _rootBranch.Clear();

	// Find a GT Recipe given pre-built ingredient lists.
	public GTRecipe? Find(List<List<AbstractMapIngredient>> list, Predicate<GTRecipe> predicate)
	{
		var iter = new RecipeIterator(this, list, predicate);
		return iter.HasNext() ? iter.Next() : null;
	}

	// Determine the correct root nodes for an ingredient - special ingredients
	// (colliding hashes) live in a separate map from the hash-unique ones.
	private static Dictionary<AbstractMapIngredient, Either<GTRecipe, Branch>> NodesForIngredient(
		AbstractMapIngredient ingredient, Branch branch) =>
		ingredient.IsSpecialIngredient() ? branch.GetSpecialNodes() : branch.GetNodes();

	// Add a recipe.
	//
	// @param recipe      the recipe to add
	// @param ingredients the ingredients in optimal order, comprising the recipe
	// @return if successful
	public bool Add(GTRecipe recipe, List<List<AbstractMapIngredient>> ingredients) =>
		AddRecursive(recipe, ingredients, _rootBranch, 0);

	// Recursively adds a recipe.
	//
	// @param recipe      the recipe to add
	// @param ingredients the ingredients to find the recipe with
	// @param branch      the branch to add ingredients to
	// @param index       the index of the ingredient list to check
	// @return if successful
	private bool AddRecursive(GTRecipe recipe, List<List<AbstractMapIngredient>> ingredients,
		Branch branch, int index)
	{
		if (index >= ingredients.Count)
			return true;
		bool lastIngredient = index == ingredients.Count - 1;
		var current = ingredients[index];
		foreach (var ingredient in current)
		{
			var nodes = NodesForIngredient(ingredient, branch);

			// Port of nodes.compute(ingredient, remappingFunction). The
			// upstream remapper never returns null here, so it reduces to:
			// keep the existing entry if present, else create a new leaf
			// (last ingredient) or a new sub-branch.
			nodes.TryGetValue(ingredient, out var v);
			Either<GTRecipe, Branch> either = lastIngredient
				// last ingredient: no existing leaf -> add the recipe; an
				// existing entry (recipe or branch) is kept - conflicts keep
				// the incumbent, exactly as upstream.
				? v ?? Either<GTRecipe, Branch>.Left(recipe)
				// not last: reuse the existing sub-branch, else make one.
				: v ?? Either<GTRecipe, Branch>.Right(new Branch());
			nodes[ingredient] = either;

			if (either.IsLeft)
			{
				if (ReferenceEquals(either.LeftValue, recipe))
					// recipe was successfully added, continue the other paths
					continue;
				// there was already a recipe here, fail on the conflict
				return false;
			}
			bool added = either.IsRight &&
			             AddRecursive(recipe, ingredients, either.RightValue, index + 1);
			if (!added)
			{
				if (lastIngredient)
				{
					// remove the recipe
					nodes.Remove(ingredient);
				}
				else
				{
					if (nodes.TryGetValue(ingredient, out var child) && child.IsRight)
					{
						var childBranch = child.RightValue;
						if (childBranch.IsEmptyBranch())
							// remove the branch if it was the only thing in it
							nodes.Remove(ingredient);
					}
				}
				return false;
			}
		}
		return true;
	}

	// One frame of the iterative trie walk.
	private sealed class SearchFrame
	{
		public int    Index;           // ingredient slot we're exploring
		public int    IngredientIndex; // position within ingredients[index]
		public Branch Branch;          // branch in the recipe DB

		public SearchFrame(int index, Branch branch)
		{
			Index           = index;
			IngredientIndex = 0;
			Branch          = branch;
		}
	}

	// Verbatim port of RecipeDB.RecipeIterator. Lazily walks the trie for
	// every recipe matching `predicate` reachable from `ingredients`.
	public sealed class RecipeIterator
	{
		private readonly RecipeDB                       _db;
		private readonly List<List<AbstractMapIngredient>> _ingredients;
		private readonly Predicate<GTRecipe>            _predicate;

		private readonly Stack<SearchFrame> _stack = new();

		private GTRecipe? _nextCached;
		private bool      _hasCached;

		public RecipeIterator(RecipeDB db, List<List<AbstractMapIngredient>> ingredients,
			Predicate<GTRecipe> predicate)
		{
			_db          = db;
			_ingredients = ingredients;
			_predicate   = predicate;

			for (int i = ingredients.Count - 1; i >= 0; i--)
				_stack.Push(new SearchFrame(i, db._rootBranch));
		}

		private GTRecipe? GetNext()
		{
			while (_stack.Count != 0)
			{
				// We stay on one frame until all ingredients have been checked.
				SearchFrame frame = _stack.Peek();

				if (frame.IngredientIndex >= _ingredients[frame.Index].Count)
				{
					_stack.Pop();
					continue;
				}

				List<AbstractMapIngredient> ingredientList = _ingredients[frame.Index];
				AbstractMapIngredient ingredient = ingredientList[frame.IngredientIndex];
				// Increment candidate pos for next iteration.
				frame.IngredientIndex++;
				var nodes  = NodesForIngredient(ingredient, frame.Branch);
				if (!nodes.TryGetValue(ingredient, out var result))
					continue;

				// Option 1: it's a recipe.
				if (result.IsLeft)
				{
					var recipe = result.LeftValue;
					if (_predicate(recipe))
						return recipe;
				}

				// Option 2: it's a branch, dive deeper.
				if (result.IsRight)
				{
					var b = result.RightValue;
					for (int j = _ingredients.Count - 1; j >= 0; j--)
						_stack.Push(new SearchFrame(j, b));
				}
			}

			return null; // no more recipes
		}

		public bool HasNext()
		{
			if (!_hasCached)
			{
				_nextCached = GetNext();
				_hasCached  = true;
			}
			return _nextCached != null;
		}

		public GTRecipe Next()
		{
			if (!_hasCached) _nextCached = GetNext();
			_hasCached = false;
			if (_nextCached == null) throw new InvalidOperationException("No more recipes");
			return _nextCached;
		}

		// Reset the iterator to walk from the start again.
		//
		// Documented deviation: upstream's reset() clears only the stack - it
		// relies on being called mid-iteration (right after the probe in
		// searchRecipe), before the look-ahead cache holds the terminal null.
		// We also clear the look-ahead cache so Reset is correct from any
		// state, including after full exhaustion.
		public void Reset()
		{
			_stack.Clear();
			for (int i = _ingredients.Count - 1; i >= 0; i--)
				_stack.Push(new SearchFrame(i, _db._rootBranch));
			_nextCached = null;
			_hasCached  = false;
		}
	}
}
