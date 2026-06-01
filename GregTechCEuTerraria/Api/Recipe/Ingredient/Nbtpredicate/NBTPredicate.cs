#nullable enable
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Recipe.Ingredient.Nbtpredicate;

// LOCKED - port of
// com.gregtechceu.gtceu.api.recipe.ingredient.nbtpredicate.NBTPredicate.
//
// Abstract predicate over a TagCompound - used by NBTPredicateIngredient to
// gate item matches by NBT contents. Subclasses encode specific shapes
// (true, has-key, equals-value, AND, OR, NOT, ...).
//
// Documented adaptation:
//   - Upstream's `CompoundTag` -> tML `TagCompound`.
//   - JSON parse + write via Mojang Gson -> System.Text.Json (lands when
//     IngredientJson.Read is wired up).
public abstract class NBTPredicate
{
	// Returns true if the given NBT satisfies this predicate.
	public abstract bool Test(TagCompound? tag);

	// Identity for JSON serialization (full upstream registry deferred to
	// the IngredientJson wave).
	public abstract string GetTypeName();
}

// Always-true predicate - the default for NBTPredicateIngredient when no
// predicate is specified (matches any NBT).
public sealed class TrueNBTPredicate : NBTPredicate
{
	public static readonly TrueNBTPredicate INSTANCE = new();
	private TrueNBTPredicate() { }

	public override bool Test(TagCompound? tag) => true;
	public override string GetTypeName() => "gtceu:true";
}
