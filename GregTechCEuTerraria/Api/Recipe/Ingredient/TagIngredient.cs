#nullable enable
using System.Collections.Generic;
using Terraria;

namespace GregTechCEuTerraria.Api.Recipe.Ingredient;

// LOCKED - Terraria-adapted port of vanilla
// `net.minecraft.world.item.crafting.Ingredient.of(TagKey<Item>)`.
//
// Concrete Ingredient subtype that matches any Terraria item belonging to
// a named tag - e.g. "forge:ingots/iron", "minecraft:planks".
//
// Documented adaptations:
//   - Upstream's `TagKey<Item>` (typed) -> string tag name. Resolution lives
//     in the GTRecipeSerializer layer + IngredientResolver /
//     VanillaSubstitution - they translate upstream tag names to lists of
//     Terraria item types.
//   - Forge's recursive tag inheritance (e.g. `#planks` includes
//     `#oak_planks`) is approximated by our suffix rules + explicit
//     mappings. Tags with no Terraria mapping return empty GetItems().
public sealed class TagIngredient : Ingredient
{
	// Upstream tag name (e.g. "forge:ingots/iron", "minecraft:planks").
	public string TagName { get; }

	// Resolved Terraria item types belonging to this tag - pre-computed at
	// construction via the resolver layer.
	private readonly IReadOnlyList<int> _types;

	// Read-only view of the resolved item types - lets the vanilla-crafting
	// bridge turn a tag into a single ingredient (1 item) or a RecipeGroup
	// (many) instead of relying on a hard-coded tag->group table.
	public IReadOnlyList<int> ResolvedTypes => _types;
	// Example Items, lazy - see GetItems().
	private IReadOnlyList<Item>? _items;

	public TagIngredient(string tagName, IReadOnlyList<int> resolvedTypes)
	{
		TagName = tagName;
		_types = resolvedTypes;
	}

	public override bool Test(Item item)
	{
		if (item is null) return false;
		foreach (var type in _types)
			if (item.type == type) return true;
		return false;
	}

	// Lazy-built - see ItemStackIngredient.GetItems for why the example stacks
	// can't be created until after tML resizes the modded item arrays.
	public override IReadOnlyList<Item> GetItems()
	{
		if (_items is null)
		{
			var examples = new List<Item>(_types.Count);
			foreach (var type in _types)
			{
				var item = new Item();
				item.SetDefaults(type);
				examples.Add(item);
			}
			_items = examples;
		}
		return _items;
	}

	public override bool IsEmpty => _types.Count == 0;

	public override string GetTypeName() => "minecraft:tag";

	public override string ToString() => $"Tag({TagName}, {_types.Count} items)";
}
