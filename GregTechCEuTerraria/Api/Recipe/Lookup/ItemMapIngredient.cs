#nullable enable
namespace GregTechCEuTerraria.Api.Recipe.Lookup;

// ADAPTED - stands in for upstream's
// com.gregtechceu.gtceu.api.recipe.lookup.ingredient.item.ItemStackMapIngredient
// + ItemTagMapIngredient.
//
// Trie node-key for one concrete Terraria item type.
//
// Documented adaptation: upstream keeps TWO item map-ingredient types - one
// keyed on an item, one keyed on a TagKey<Item> - because MC items carry a
// live tag set, so a query ItemStack produces both its item-key AND a tag-key
// per tag. Terraria items have no live tag set, and our TagIngredient already
// pre-resolves to a flat list of item-type ints (TagIngredient.ResolvedTypes).
// So the trie keys purely on concrete item type: a recipe's tag ingredient is
// expanded at compile time into one ItemMapIngredient per resolved type
// (RecipeLookupCompiler.DecomposeItem), and there is no separate tag key.
public sealed class ItemMapIngredient : AbstractMapIngredient
{
	// Terraria item-type id (Item.type).
	public readonly int ItemType;

	public ItemMapIngredient(int itemType)
	{
		ItemType = itemType;
	}

	protected override int Hash() => ItemType * 31;

	protected override bool EqualsSameClass(AbstractMapIngredient other) =>
		// EqualsSameClass is only called when `other` is the same class.
		ItemType == ((ItemMapIngredient)other).ItemType;

	public override string ToString() => $"ItemMapIngredient{{type={ItemType}}}";
}
