#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Fluids.Attribute;

namespace GregTechCEuTerraria.Api.Recipe.Ingredient;

// LOCKED - Terraria-adapted port of
// com.gregtechceu.gtceu.api.recipe.ingredient.FluidIngredient.
//
// Predicate over a FluidType - equivalent of vanilla MC's Ingredient but
// for fluids. Supports:
//   - exact match (one FluidType + amount)
//   - tag match (multiple FluidTypes via tag - adapted to our IngredientResolver layer)
//   - attribute match (any fluid carrying a specific FluidAttribute, e.g. ACID)
//
// Documented adaptations:
//   - Forge FluidStack -> our FluidStack (we already have).
//   - Forge fluid-tag tree -> IngredientResolver layer.
//   - Codec/serializer dropped - System.Text.Json dispatch.
//   - The upstream class is 406 LOC; the bulk is Codec / NBT-tag / partial-
//     matching serialization. The pure-math predicate + amount surface is
//     what we need.
public class FluidIngredient : Ingredient
{
	// One of these three is non-null at construction:
	//   ExactType  - exact-fluid match
	//   TagName    - tag name (resolved to fluid type list at construction)
	//   Attribute  - attribute match (any fluid carrying the named attribute)
	public FluidType? ExactType { get; }
	public string? TagName { get; }
	public FluidAttribute? Attribute { get; }

	// Required amount in mB. Recipe-match compares against the consumed/produced amount.
	public int Amount { get; set; }

	// Resolved list of fluid types matching this ingredient - precomputed
	// at construction via the resolver layer. Used by GetFluids() and by
	// the recipe browser for display.
	private readonly IReadOnlyList<FluidType> _matchingFluids;

	public FluidIngredient(FluidType exact, int amount)
	{
		ExactType = exact;
		Amount = amount;
		_matchingFluids = new[] { exact };
	}

	public FluidIngredient(string tagName, IReadOnlyList<FluidType> resolvedFluids, int amount)
	{
		TagName = tagName;
		Amount = amount;
		_matchingFluids = resolvedFluids;
	}

	public FluidIngredient(FluidAttribute attribute, IReadOnlyList<FluidType> resolvedFluids, int amount)
	{
		Attribute = attribute;
		Amount = amount;
		_matchingFluids = resolvedFluids;
	}

	// Test a FluidType for membership in this ingredient.
	public bool TestFluid(FluidType? fluid)
	{
		if (fluid is null) return false;
		if (ExactType is not null) return ReferenceEquals(fluid, ExactType) || fluid.Id == ExactType.Id;
		if (Attribute is not null) return fluid.HasAttribute(Attribute);
		// Tag-resolved list
		foreach (var f in _matchingFluids)
			if (f.Id == fluid.Id) return true;
		return false;
	}

	// Test a FluidStack - must match the fluid type AND carry at least the
	// required amount.
	public bool TestStack(FluidStack stack) =>
		!stack.IsEmpty && TestFluid(stack.Type) && stack.Amount >= Amount;

	public IReadOnlyList<FluidType> GetFluids() => _matchingFluids;

	// Verbatim port of upstream's getStacks() - materialises each matching
	// FluidType into a FluidStack at this ingredient's required Amount. Used
	// by output-display code that wants concrete (type, amount) pairs.
	public FluidStack[] GetStacks()
	{
		var result = new FluidStack[_matchingFluids.Count];
		for (int i = 0; i < _matchingFluids.Count; i++)
			result[i] = new FluidStack(_matchingFluids[i], Amount);
		return result;
	}

	// Inherited Ingredient.Test/GetItems are for items. Fluid ingredients
	// never match Terraria items; recipe-handler dispatch routes fluid
	// ingredients to NotifiableFluidTank, not NotifiableItemStackHandler.
	public override bool Test(Terraria.Item item) => false;
	public override IReadOnlyList<Terraria.Item> GetItems() => System.Array.Empty<Terraria.Item>();

	public override bool IsEmpty => _matchingFluids.Count == 0 || Amount <= 0;

	public override string GetTypeName() => "gtceu:fluid";

	public override string ToString() =>
		ExactType is not null ? $"FluidIngredient({ExactType.Id} x {Amount}mB)" :
		TagName  is not null ? $"FluidIngredient({TagName} x {Amount}mB)" :
		Attribute is not null ? $"FluidIngredient(@{Attribute.Id} x {Amount}mB)" :
		"FluidIngredient(EMPTY)";
}
