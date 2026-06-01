#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Fluids;
using Terraria;
using Terraria.ID;

namespace GregTechCEuTerraria.Api.Recipe.Ingredient;

// Port of com.gregtechceu.gtceu.api.recipe.ingredient.FluidContainerIngredient.
//
// An item ingredient that matches any item which IS a fluid container holding
// the wrapped FluidIngredient's fluid, in sufficient amount.
//
// Documented adaptations:
//   - Forge's capability machinery (FluidUtil.getFluidContained, the
//     IFluidHandlerItem capability *registry*) -> an IFluidHandlerItem cast for
//     our own items plus a fixed vanilla-bucket table. Terraria has no
//     capability registry; ModItem pattern-matching is the lookup - exactly
//     how the rest of this port substitutes for Forge capabilities.
//   - getItems / getFilledBucket candidate list -> vanilla buckets for the
//     matching fluids. Filled fluid cells carry their fluid in NBT and
//     Terraria recipe matching is type-based, so buckets are the only clean
//     candidate representation.
//   - tryEmptyContainer "can this be emptied" gate dropped - every item that
//     reports contained fluid here is one we can drain.
public sealed class FluidContainerIngredient : Ingredient
{
	public FluidIngredient Fluid { get; }

	public FluidContainerIngredient(FluidIngredient fluid) => Fluid = fluid;

	public override bool Test(Item item)
	{
		if (item is null || item.IsAir) return false;
		var contained = GetFluidContained(item);
		return !contained.IsEmpty
		    && Fluid.TestStack(contained)
		    && contained.Amount >= Fluid.Amount;
	}

	private IReadOnlyList<Item>? _cachedItems;

	public override IReadOnlyList<Item> GetItems()
	{
		if (_cachedItems is not null) return _cachedItems;
		var items = new List<Item>();
		foreach (var fluid in Fluid.GetFluids())
		{
			int bucket = VanillaBucketFor(fluid);
			if (bucket == 0) continue;
			var stack = new Item();
			stack.SetDefaults(bucket);
			items.Add(stack);
		}
		return _cachedItems = items;
	}

	public override bool IsEmpty => Fluid.IsEmpty;

	public override string GetTypeName() => "gtceu:fluid_container";

	// Forge FluidUtil.getFluidContained, adapted: our IFluidHandlerItem items
	// report their own NBT contents; vanilla buckets map to a fixed fluid.
	private static FluidStack GetFluidContained(Item item)
	{
		if (item.ModItem is IFluidHandlerItem handler)
			return handler.GetTank(0);

		string? vanillaFluid = item.type switch
		{
			ItemID.WaterBucket => "water",
			ItemID.LavaBucket  => "lava",
			ItemID.HoneyBucket => "honey",
			_                  => null,
		};
		if (vanillaFluid is not null && FluidRegistry.TryGet(vanillaFluid, out var type))
			return new FluidStack(type, 1000);
		return FluidStack.Empty;
	}

	private static int VanillaBucketFor(FluidType? fluid) => fluid?.Id switch
	{
		"water" => ItemID.WaterBucket,
		"lava"  => ItemID.LavaBucket,
		"honey" => ItemID.HoneyBucket,
		_       => 0,
	};
}
