#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Fluids;

namespace GregTechCEuTerraria.Api.Recipe.Ingredient;

// Resolver bridge - translates upstream item / tag / fluid IDs to Terraria-
// side types at Ingredient construction time. Implementations live in the
// mod layer (Common/Mod) since the actual resolution touches our
// IngredientResolver / VanillaSubstitution / FluidRegistry / RecipeGroup
// translation tables.
//
// IngredientJson.Read takes one of these and uses it to materialize concrete
// Ingredient subclasses with resolved Terraria values.
public interface IIngredientResolver
{
	// Resolve an upstream item id (e.g. "minecraft:iron_ingot",
	// "gtceu:iron_dust") to a Terraria item type id. Returns 0
	// (ItemID.None) if unresolved.
	int ResolveItemType(string upstreamId);

	// Resolve an upstream item tag (e.g. "forge:ingots/iron",
	// "minecraft:planks") to a list of matching Terraria item types.
	// Returns empty if the tag has no Terraria mapping.
	IReadOnlyList<int> ResolveItemTag(string tagName);

	// Resolve an upstream fluid id (e.g. "minecraft:water",
	// "gtceu:molten_iron") to a registered FluidType. Returns null if
	// unresolved.
	FluidType? ResolveFluidType(string upstreamId);

	// Resolve an upstream fluid tag to a list of registered FluidTypes.
	// Returns empty if no Terraria mapping.
	IReadOnlyList<FluidType> ResolveFluidTag(string tagName);
}
