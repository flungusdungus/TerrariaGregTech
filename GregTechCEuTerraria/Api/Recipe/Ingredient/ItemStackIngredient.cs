#nullable enable
using System.Collections.Generic;
using Terraria;

namespace GregTechCEuTerraria.Api.Recipe.Ingredient;

// LOCKED - Terraria-adapted port of vanilla
// `net.minecraft.world.item.crafting.Ingredient.of(ItemStack...)`.
//
// Concrete Ingredient subtype that matches one specific Terraria item type.
// Upstream's Ingredient supports an OR of multiple ItemStack examples; we
// model multi-stack-OR via a list of these (or via TagIngredient).
//
// Documented adaptations:
//   - Upstream's `Item` (MC item type) -> Terraria item-type id (int).
//   - Upstream's per-ItemStack NBT comparison is delegated to
//     NBTPredicateIngredient; this base ingredient only checks item type.
//   - JSON shape `{"item": "minecraft:iron_ingot"}` -> resolved via
//     IngredientResolver at GTRecipeSerializer time; this class holds only
//     the resolved Terraria type.
public sealed class ItemStackIngredient : Ingredient
{
	// Resolved Terraria item type id (Terraria.ID.ItemID.* / ModContent.ItemType<>()).
	public int ItemType { get; }

	// Original upstream id string (e.g. "minecraft:iron_ingot",
	// "gtceu:iron_dust"). Carried for debugging + recipe-browser tooltip
	// display. Empty if not known at construction (synthesized ingredients).
	public string UpstreamId { get; }

	// Lazy - see GetItems().
	private IReadOnlyList<Item>? _exampleList;

	public ItemStackIngredient(int itemType, string upstreamId = "")
	{
		ItemType = itemType;
		UpstreamId = upstreamId;
	}

	public override bool Test(Item item) =>
		item is not null && item.type == ItemType;

	// Lazy-built. `Item.SetDefaults` indexes `ItemID.Sets.*` arrays that tML
	// only resizes for modded item types AFTER every mod's Load() - and the
	// recipe JSON is parsed inside Mod.Load(). Building the example stack in
	// the constructor therefore IndexOutOfRange'd for every modded-item
	// ingredient. Deferring to first GetItems() (recipe-browser / match time,
	// post-resize) also mirrors how upstream's Ingredient caches `itemStacks`.
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

	public override string GetTypeName() => "minecraft:item";

	public override string ToString() => $"ItemStack({(UpstreamId.Length > 0 ? UpstreamId : ItemType.ToString())})";
}
