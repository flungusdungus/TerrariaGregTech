#nullable enable
using System.Collections.Generic;
using Terraria;

namespace GregTechCEuTerraria.Api.Recipe.Ingredient;

// LOCKED - Terraria-adapted port of net.minecraft.world.item.crafting.Ingredient
// + Forge extensions. DO NOT modify behavior; mirror upstream changes only.
//
// Abstract predicate over a Terraria item. Concrete subclasses:
//   - ItemStackIngredient(Item) - matches exact item type
//   - TagIngredient(string)     - matches any item in the named tag
//   - NBTPredicateIngredient    - matches by NBT predicate
//   - IntCircuitIngredient(N)   - matches the machine's programmed-circuit
//                                  state set to N (adapted; upstream matches
//                                  against a ProgrammedCircuit item slot)
//   - SizedIngredient           - wraps another Ingredient + count
//   - IntProviderIngredient     - wraps Ingredient + IntProvider count
//
// JSON dispatch via IngredientJson.Read - `type:` discriminator routes to
// the matching concrete class.
//
// Documented adaptations:
//   - ItemStack -> Terraria Item (mostly drop-in: both are mutable-with-NBT
//     value carriers).
//   - Forge tag tree -> our IngredientResolver / VanillaSubstitution tag
//     translation layer.
//   - Mojang Codec / Forge IIngredientSerializer -> System.Text.Json dispatch.
public abstract class Ingredient
{
	// True if this ingredient matches the given Terraria item.
	public abstract bool Test(Item item);

	// Example items that satisfy this ingredient - used by recipe-browser
	// UIs to render the alternatives, and by output-side recipes to
	// instantiate the result.
	public abstract IReadOnlyList<Item> GetItems();

	// True if this ingredient has no matching items (failed resolution).
	public virtual bool IsEmpty => GetItems().Count == 0;

	// JSON type discriminator. One of:
	//   "minecraft:item"        (ItemStackIngredient)
	//   "minecraft:tag"         (TagIngredient)
	//   "forge:nbt"             (NBTPredicateIngredient)
	//   "gtceu:circuit"         (IntCircuitIngredient)
	//   "gtceu:sized"           (SizedIngredient)
	//   "gtceu:int_provider"    (IntProviderIngredient)
	public abstract string GetTypeName();
}
