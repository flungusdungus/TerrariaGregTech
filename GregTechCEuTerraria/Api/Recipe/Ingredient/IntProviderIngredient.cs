#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Util.ValueProviders;
using Terraria;

namespace GregTechCEuTerraria.Api.Recipe.Ingredient;

// LOCKED - port of
// com.gregtechceu.gtceu.api.recipe.ingredient.IntProviderIngredient.
// DO NOT modify behavior; mirror upstream changes only.
//
// Wraps another Ingredient with a runtime-rolled count. Count is sampled
// from the IntProvider when GetItems is first called per cycle, cached as
// SampledCount so the same value is reused within the cycle (simulate +
// execute pass both see the same rolled count), then reset for the next
// cycle via SetSampledCount(-1) / SetItemStacks(null).
//
// Recipe handler integration (matches upstream
// NotifiableItemStackHandler.HandleRecipe):
//   1. SetItemStacks(null) + SetSampledCount(-1)   // reset state
//   2. if simulate: amount = GetMaxSizeStack().Count  // upper bound
//      else:         items = GetItems() (rolls the count)
//   3. amount = items[0].count
//
// Documented adaptations:
//   - Forge Codec / IIngredientSerializer dropped (System.Text.Json dispatch).
//   - ItemStack -> Terraria Item; count manipulation via Item.stack field.
public class IntProviderIngredient : Ingredient, IRangedIngredient
{
	public IntProvider CountProvider { get; }

	// -1 = not yet rolled. Set by RollSampledCount, cleared by recipe
	// handler at cycle boundary.
	public int SampledCount { get; set; } = -1;

	public Ingredient Inner { get; }

	// Cached materialized stacks at the sampled count. Cleared (null) by
	// the recipe handler at cycle boundary; lazily populated by GetItems.
	private Item[]? _itemStacks;

	private static readonly Random _rng = new();

	protected IntProviderIngredient(Ingredient inner, IntProvider countProvider)
	{
		Inner = inner;
		CountProvider = countProvider;
	}

	public static IntProviderIngredient Of(Ingredient inner, IntProvider countProvider)
	{
		if (countProvider.GetMinValue() < 0)
			throw new ArgumentException("IntProviderIngredient must have a min value of at least 0.");
		return new IntProviderIngredient(inner, countProvider);
	}

	public override bool Test(Item item) => Inner.Test(item);

	// Verbatim port of upstream's getItems():
	//   - On first call, samples count from IntProvider via RollSampledCount.
	//   - Materializes a copy of every inner example item with the sampled
	//     count.
	//   - If count rolls to 0, returns empty.
	public override IReadOnlyList<Item> GetItems()
	{
		if (_itemStacks is null)
		{
			int cachedCount = RollSampledCount();
			if (cachedCount == 0) return Array.Empty<Item>();
			var inner = Inner.GetItems();
			_itemStacks = new Item[inner.Count];
			for (int i = 0; i < inner.Count; i++)
			{
				var copy = inner[i].Clone();
				copy.stack = cachedCount;
				_itemStacks[i] = copy;
			}
		}
		return _itemStacks;
	}

	// Verbatim port - returns inner stacks at maximum possible count (used
	// by recipe-match simulate path to gauge upper bound).
	public IReadOnlyList<Item> GetMaxSizeStack()
	{
		int max = CountProvider.GetMaxValue();
		var inner = Inner.GetItems();
		var result = new Item[inner.Count];
		for (int i = 0; i < inner.Count; i++)
		{
			var copy = inner[i].Clone();
			copy.stack = max;
			result[i] = copy;
		}
		return result;
	}

	// Verbatim port - samples count from IntProvider; caches as SampledCount.
	public int RollSampledCount() => RollSampledCount(_rng);

	public int RollSampledCount(Random random)
	{
		if (SampledCount < 0)
			SampledCount = CountProvider.Sample(random);
		return SampledCount;
	}

	// Set the materialized-stacks cache (recipe handler clears via null).
	public void SetItemStacks(Item[]? stacks) => _itemStacks = stacks;

	// === IRangedIngredient ===================================================
	public IntProvider GetCountProvider() => CountProvider;
	public int GetSampledCount() => SampledCount;
	public void SetSampledCount(int count) => SampledCount = count;
	// Concrete form so it's callable through the concrete type - C# default
	// interface methods are only callable through the interface reference.
	public double GetMidRoll() => (CountProvider.GetMaxValue() + CountProvider.GetMinValue()) / 2.0;
	public void Reset()
	{
		SampledCount = -1;
		_itemStacks = null;
	}

	public override bool IsEmpty => Inner.IsEmpty || CountProvider.GetMaxValue() == 0;

	public override string GetTypeName() => "gtceu:int_provider";

	public override string ToString() => $"IntProviderIngredient(inner={Inner}, count={CountProvider})";
}
