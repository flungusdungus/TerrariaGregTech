#nullable enable
using System.Collections.Generic;
using Terraria;

namespace GregTechCEuTerraria.Api.Recipe.Ingredient;

// Port of com.gregtechceu.gtceu.api.recipe.ingredient.IntCircuitIngredient.
//
// Item ingredient that matches a `programmed_circuit` (IntCircuitItem) with a
// specific Configuration value. Recipe matching is the standard item-vs-handler
// Test() - the controller / single-block walks its attached item handlers
// (importItems + circuitInventory[0]) and Test() returns true for any stack
// that is a configured IntCircuitItem with the matching value.
//
// Adaptation: upstream extends StrictNBTIngredient with the ProgrammedCircuit
// item as the backing stack; we implement Test() directly because our
// Ingredient hierarchy has no StrictNBT parent. GetItems() returns the
// canonical IntCircuitItem stack at the configured value for the recipe
// browser.
//
// Cached singletons per configuration value (matches upstream INGREDIENTS[]).
public sealed class IntCircuitIngredient : Ingredient
{
	public const int CIRCUIT_MIN = 0;
	public const int CIRCUIT_MAX = 32;

	private static readonly IntCircuitIngredient?[] _cache = new IntCircuitIngredient?[CIRCUIT_MAX + 1];

	public int Configuration { get; }

	private Item[]? _stacksCache;

	private IntCircuitIngredient(int configuration) { Configuration = configuration; }

	public static IntCircuitIngredient Of(int configuration)
	{
		if (configuration < CIRCUIT_MIN || configuration > CIRCUIT_MAX)
			throw new System.IndexOutOfRangeException(
				$"Circuit configuration {configuration} is out of range [{CIRCUIT_MIN}..{CIRCUIT_MAX}]");
		return _cache[configuration] ??= new IntCircuitIngredient(configuration);
	}

	public override bool Test(Item item)
	{
		if (item == null || item.IsAir) return false;
		return item.ModItem is TerrariaCompat.Items.IntCircuitItem circuit
		    && circuit.Configuration == Configuration;
	}

	public override IReadOnlyList<Item> GetItems()
	{
		if (_stacksCache != null) return _stacksCache;
		int type = Terraria.ModLoader.ModContent.ItemType<TerrariaCompat.Items.IntCircuitItem>();
		if (type <= 0) return System.Array.Empty<Item>();
		var stack = new Item();
		stack.SetDefaults(type);
		if (stack.ModItem is TerrariaCompat.Items.IntCircuitItem ic) ic.Configuration = Configuration;
		return _stacksCache = new[] { stack };
	}

	public override bool IsEmpty => false;

	public override string GetTypeName() => "gtceu:circuit";

	public override string ToString() => $"IntCircuitIngredient({Configuration})";
}
