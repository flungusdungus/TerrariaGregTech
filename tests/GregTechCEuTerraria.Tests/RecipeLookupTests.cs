using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Lookup;
using Xunit;

namespace GregTechCEuTerraria.Tests
{
	// Pure-logic tests for the RecipeLookup trie (Api/Recipe/Lookup/) - RecipeDB
	// + Branch + AbstractMapIngredient + the Item/Fluid/Circuit map ingredients.
	//
	// Ports the cases of upstream's GTRecipeLookupTest (an MC GameTest that
	// can't run here) against RecipeDB directly: simple success/failure,
	// false predicate, extra ingredients, custom predicate. Adds checks for our
	// adaptations: tag expansion (one recipe indexed under several keys),
	// add-conflict handling, and the hash-collision safety of the same-class
	// equality gate.
	//
	// The real GTRecipe pulls in tML's TagCompound and can't compile in this
	// pure-logic project - the GTRecipe stub below stands in. RecipeDB only
	// ever uses a recipe as an opaque reference-typed payload, so a bare
	// identity class is a faithful stand-in.
	public class RecipeLookupTests
	{
		// Arbitrary distinct item-type ids.
		private const int Cobble = 1, Stone = 2, Acacia = 3, Birch = 4, Cherry = 5,
		                  RedstoneTorch = 6, A = 7, B = 8;

		private static readonly Predicate<GTRecipe> AlwaysTrue  = _ => true;
		private static readonly Predicate<GTRecipe> AlwaysFalse = _ => false;

		// --- helpers -------------------------------------------------------
		private static GTRecipe R(string id) => new(id);
		private static ItemMapIngredient ItemKey(int type) => new(type);

		// One recipe/query slot - a set of alternative keys.
		private static List<AbstractMapIngredient> Slot(params AbstractMapIngredient[] keys) =>
			new(keys);

		// A full ingredient list - one slot per input.
		private static List<List<AbstractMapIngredient>> Ings(params List<AbstractMapIngredient>[] slots) =>
			new(slots);

		// --- trie lookup ---------------------------------------------------

		[Fact]
		public void SimpleSuccess_FindsRecipeByItsIngredient()
		{
			var db = new RecipeDB();
			var smelt = R("smelt_stone");
			Assert.True(db.Add(smelt, Ings(Slot(ItemKey(Cobble)))));

			Assert.Same(smelt, db.Find(Ings(Slot(ItemKey(Cobble))), AlwaysTrue));
		}

		[Fact]
		public void SimpleFailure_UnrelatedIngredientFindsNothing()
		{
			var db = new RecipeDB();
			db.Add(R("smelt_stone"), Ings(Slot(ItemKey(Cobble))));

			Assert.Null(db.Find(Ings(Slot(ItemKey(RedstoneTorch))), AlwaysTrue));
		}

		[Fact]
		public void FalsePredicate_FindsNothing()
		{
			var db = new RecipeDB();
			db.Add(R("smelt_stone"), Ings(Slot(ItemKey(Cobble))));

			Assert.Null(db.Find(Ings(Slot(ItemKey(Cobble))), AlwaysFalse));
		}

		[Fact]
		public void ExtraUnrelatedIngredients_StillFinds()
		{
			var db = new RecipeDB();
			var smelt = R("smelt_stone");
			db.Add(smelt, Ings(Slot(ItemKey(Cobble))));

			// query carries an extra item the recipe doesn't use
			Assert.Same(smelt,
				db.Find(Ings(Slot(ItemKey(Cobble)), Slot(ItemKey(RedstoneTorch))), AlwaysTrue));
		}

		[Fact]
		public void EmptyDb_FindsNothing()
		{
			Assert.Null(new RecipeDB().Find(Ings(Slot(ItemKey(Cobble))), AlwaysTrue));
		}

		[Fact]
		public void MultiInputRecipe_RequiresEveryInput()
		{
			var db = new RecipeDB();
			var r = R("a_plus_b");
			db.Add(r, Ings(Slot(ItemKey(A)), Slot(ItemKey(B))));

			// both inputs available - found, regardless of query slot order
			Assert.Same(r, db.Find(Ings(Slot(ItemKey(A)), Slot(ItemKey(B))), AlwaysTrue));
			Assert.Same(r, db.Find(Ings(Slot(ItemKey(B)), Slot(ItemKey(A))), AlwaysTrue));

			// only one input available - not found
			Assert.Null(db.Find(Ings(Slot(ItemKey(A))), AlwaysTrue));
			Assert.Null(db.Find(Ings(Slot(ItemKey(B))), AlwaysTrue));
		}

		[Fact]
		public void TagExpansion_RecipeIndexedUnderEveryAlternativeKey()
		{
			// Our adaptation: a tag ingredient compiles to one slot with
			// several alternative item keys (RecipeLookupCompiler expands
			// TagIngredient.ResolvedTypes).
			var db = new RecipeDB();
			var r = R("smelt_any_wood");
			db.Add(r, Ings(Slot(ItemKey(Acacia), ItemKey(Birch), ItemKey(Cherry))));

			Assert.Same(r, db.Find(Ings(Slot(ItemKey(Acacia))), AlwaysTrue));
			Assert.Same(r, db.Find(Ings(Slot(ItemKey(Birch))),  AlwaysTrue));
			Assert.Same(r, db.Find(Ings(Slot(ItemKey(Cherry))), AlwaysTrue));
			Assert.Null(db.Find(Ings(Slot(ItemKey(Stone))), AlwaysTrue));
		}

		[Fact]
		public void Conflict_SecondRecipeOnSameExactPathIsRejected()
		{
			var db = new RecipeDB();
			var first = R("first");
			Assert.True(db.Add(first, Ings(Slot(ItemKey(Cobble)))));
			// identical single-key path -> conflict; the incumbent is kept
			Assert.False(db.Add(R("second"), Ings(Slot(ItemKey(Cobble)))));
			Assert.Same(first, db.Find(Ings(Slot(ItemKey(Cobble))), AlwaysTrue));
		}

		[Fact]
		public void CustomPredicate_SelectsMatchingRecipe()
		{
			var db = new RecipeDB();
			db.Add(R("wanted"), Ings(Slot(ItemKey(Cobble))));

			Assert.Equal("wanted",
				db.Find(Ings(Slot(ItemKey(Cobble))), r => r.Id == "wanted")?.Id);
			Assert.Null(db.Find(Ings(Slot(ItemKey(Cobble))), r => r.Id == "other"));
		}

		[Fact]
		public void Iterator_YieldsEveryReachableRecipe()
		{
			var db = new RecipeDB();
			var r1 = R("r1");
			var r2 = R("r2");
			db.Add(r1, Ings(Slot(ItemKey(Cobble))));
			db.Add(r2, Ings(Slot(ItemKey(Stone))));

			var iter = new RecipeDB.RecipeIterator(
				db, Ings(Slot(ItemKey(Cobble)), Slot(ItemKey(Stone))), AlwaysTrue);
			var found = new List<GTRecipe>();
			while (iter.HasNext()) found.Add(iter.Next());

			Assert.Contains(r1, found);
			Assert.Contains(r2, found);

			// Reset re-walks from scratch.
			iter.Reset();
			Assert.True(iter.HasNext());
		}

		// --- map-ingredient identity ---------------------------------------

		[Fact]
		public void MapIngredient_SameClassEqualityGate()
		{
			// ItemMapIngredient(t) hashes to t*31; CircuitMapIngredient(t*31)
			// collides. Equality must still gate on the concrete class first.
			var item    = new ItemMapIngredient(1);     // hash 31
			var circuit = new CircuitMapIngredient(31); // hash 31
			Assert.Equal(item.GetHashCode(), circuit.GetHashCode());
			Assert.False(item.Equals(circuit));
			Assert.False(circuit.Equals(item));

			Assert.Equal(new ItemMapIngredient(5), new ItemMapIngredient(5));
			Assert.NotEqual(new ItemMapIngredient(5), new ItemMapIngredient(6));
		}

		[Fact]
		public void HashCollidingKeys_OfDifferentClassesStaySeparateInTrie()
		{
			var db = new RecipeDB();
			var itemRecipe    = R("item_recipe");
			var circuitRecipe = R("circuit_recipe");
			// ItemMapIngredient(1) and CircuitMapIngredient(31) both hash to 31.
			db.Add(itemRecipe,    Ings(Slot(new ItemMapIngredient(1))));
			db.Add(circuitRecipe, Ings(Slot(new CircuitMapIngredient(31))));

			Assert.Same(itemRecipe,
				db.Find(Ings(Slot(new ItemMapIngredient(1))), AlwaysTrue));
			Assert.Same(circuitRecipe,
				db.Find(Ings(Slot(new CircuitMapIngredient(31))), AlwaysTrue));
		}

		[Fact]
		public void MixedCapabilities_ItemFluidCircuitInOneRecipe()
		{
			var db = new RecipeDB();
			var r = R("mixed");
			db.Add(r, Ings(
				Slot(new ItemMapIngredient(Cobble)),
				Slot(new FluidMapIngredient("water")),
				Slot(new CircuitMapIngredient(2))));

			// all three present -> found
			Assert.Same(r, db.Find(Ings(
				Slot(new ItemMapIngredient(Cobble)),
				Slot(new FluidMapIngredient("water")),
				Slot(new CircuitMapIngredient(2))), AlwaysTrue));

			// wrong circuit -> not found
			Assert.Null(db.Find(Ings(
				Slot(new ItemMapIngredient(Cobble)),
				Slot(new FluidMapIngredient("water")),
				Slot(new CircuitMapIngredient(9))), AlwaysTrue));
		}
	}
}

namespace GregTechCEuTerraria.Api.Recipe
{
	// Test stub for the real GTRecipe (which pulls in tML's TagCompound and so
	// can't compile in this pure-logic test project). RecipeDB only ever uses a
	// recipe as an opaque reference-typed payload (ReferenceEquals), so a bare
	// identity class is a faithful stand-in.
	public sealed class GTRecipe
	{
		public readonly string Id;
		public GTRecipe(string id) => Id = id;
		public override string ToString() => Id;
	}
}
