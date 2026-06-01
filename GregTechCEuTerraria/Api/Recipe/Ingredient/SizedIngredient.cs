#nullable enable
using System.Collections.Generic;
using Terraria;

namespace GregTechCEuTerraria.Api.Recipe.Ingredient;

// LOCKED - port of
// com.gregtechceu.gtceu.api.recipe.ingredient.SizedIngredient.
// DO NOT modify behavior; mirror upstream changes only.
//
// Wraps another Ingredient with a fixed count. Recipe-match dispatch reads
// the count via GetAmount(); SetAmount is used during handle-recipe to
// decrement the remaining count when a partial match has been consumed.
//
// Documented adaptations:
//   - Upstream maintains an `itemStacks[]` cache + `changed` dirty flag for
//     count-multiplied display copies. Our GetItems() delegates to inner; if
//     count-multiplied display is needed, that's a UI-layer concern (recipe
//     browser materializes the display stack at render time).
//   - Forge IngredientAccessor / TagValueAccessor / ItemValueAccessor
//     mixins dropped - those were upstream's workaround for Forge's
//     private fields. We expose the wrapped Ingredient directly via Inner.
public class SizedIngredient : Ingredient
{
	public Ingredient Inner { get; }
	public int Amount { get; set; }

	public SizedIngredient(Ingredient inner, int amount)
	{
		Inner = inner;
		Amount = amount;
	}

	public static SizedIngredient Create(Ingredient inner, int amount) => new(inner, amount);
	public static SizedIngredient Create(Ingredient inner) => new(inner, 1);

	public override bool Test(Item item) => Inner.Test(item);
	public override IReadOnlyList<Item> GetItems() => Inner.GetItems();
	public override bool IsEmpty => Inner.IsEmpty;
	public override string GetTypeName() => "gtceu:sized";
}
