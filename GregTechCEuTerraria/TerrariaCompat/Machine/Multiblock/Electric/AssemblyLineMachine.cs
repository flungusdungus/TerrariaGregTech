#nullable enable
using System.Collections.Generic;
using System.Linq;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine.Feature.Multiblock;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Content;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.Config;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Electric;

// Port of AssemblyLineMachine. Ordered-on-input: N-th item input -> N-th
// left-to-right bus; N-th fluid -> N-th hatch. WMM input hooks overridden
// (AsslineRecipeLogic collapsed). Per-bus drain = upstream's two-pass
// simulate-then-real.
public sealed class AssemblyLineMachine : WorkableElectricMultiblockMachine
{
	protected override string Label => "Assembly Line";

	private readonly List<NotifiableItemStackHandler> _orderedItemBuses    = new();
	private readonly List<NotifiableFluidTank>        _orderedFluidHatches = new();

	public AssemblyLineMachine() : base() { }

	private bool _allowCircuitSlots = false;
	public override bool AllowCircuitSlots() => _allowCircuitSlots;
	public void SetAllowCircuitSlots(bool value) => _allowCircuitSlots = value;

	// X-ascending = upstream RelativeDirection.RIGHT. Y secondary for determinism.
	public override System.Comparison<IMultiPart> GetPartSorter() => (a, b) =>
	{
		var pa = a.Self()?.Position; var pb = b.Self()?.Position;
		if (pa is null && pb is null) return 0;
		if (pa is null) return -1;
		if (pb is null) return  1;
		int dx = pa.Value.X.CompareTo(pb.Value.X);
		return dx != 0 ? dx : pa.Value.Y.CompareTo(pb.Value.Y);
	};

	public override void SaveData(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.SaveData(tag);
		tag["allowCircuitSlots"] = _allowCircuitSlots;
	}

	public override void LoadData(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.LoadData(tag);
		if (tag.ContainsKey("allowCircuitSlots"))
			_allowCircuitSlots = tag.GetBool("allowCircuitSlots");
	}

	public override void OnStructureFormed()
	{
		base.OnStructureFormed();
		RebuildBusOrdering();
	}

	public override void OnStructureInvalid()
	{
		base.OnStructureInvalid();
		_orderedItemBuses.Clear();
		_orderedFluidHatches.Clear();
	}

	public override void OnPartUnload()
	{
		base.OnPartUnload();
		_orderedItemBuses.Clear();
		_orderedFluidHatches.Clear();
	}

	private void RebuildBusOrdering()
	{
		_orderedItemBuses.Clear();
		_orderedFluidHatches.Clear();
		// GetParts is X-sorted (GetPartSorter). ShouldSearchContent = upstream filter.
		foreach (var part in GetParts())
		{
			if (part is ItemBusPartMachine bus && bus.Io == IO.IN
				&& bus.Inventory != null && bus.Inventory.ShouldSearchContent)
				_orderedItemBuses.Add(bus.Inventory);
			if (part is FluidHatchPartMachine hatch && hatch.Io == IO.IN
				&& hatch.Tank != null && hatch.Tank.ShouldSearchContent)
				_orderedFluidHatches.Add(hatch.Tank);
		}
	}

	// Verbatim AsslineRecipeLogic.matchRecipe gating: unordered match first
	// (RecipeHelper.matchContents), then positional ordering per-flag.
	public override ActionResult TryMatchInputContents(
		GTRecipe recipe,
		IReadOnlyList<Content> items,
		IReadOnlyList<Content> fluids)
	{
		var baseResult = base.TryMatchInputContents(recipe, items, fluids);
		if (!baseResult.IsSuccess) return baseResult;

		bool orderedItems  = GTConfig.Instance?.OrderedAssemblyLineItems  ?? true;
		bool orderedFluids = GTConfig.Instance?.OrderedAssemblyLineFluids ?? false;
		if (!orderedItems && !orderedFluids) return ActionResult.SUCCESS;
		// Item check is unconditional after both-off guard (verbatim upstream).
		if (!CheckOrderedItems(items))
			return ActionResult.Fail(null, ItemRecipeCapability.CAP, IO.IN);
		if (!orderedFluids) return ActionResult.SUCCESS;
		if (!CheckOrderedFluids(fluids))
			return ActionResult.Fail(null, FluidRecipeCapability.CAP, IO.IN);
		return ActionResult.SUCCESS;
	}

	// Verbatim AsslineRecipeLogic.consumeAll per-cap split. The unordered branch
	// routes a single-cap list through base consume (empty for the other),
	// mirroring upstream copyWithItems/copyWithFluids -> RecipeHelper.handleRecipeIO.
	public override ActionResult TryConsumeInputContents(
		GTRecipe recipe,
		IReadOnlyList<Content> items,
		IReadOnlyList<Content> fluids)
	{
		bool orderedItems  = GTConfig.Instance?.OrderedAssemblyLineItems  ?? true;
		bool orderedFluids = GTConfig.Instance?.OrderedAssemblyLineFluids ?? false;
		var none = System.Array.Empty<Content>();

		if (items.Count > 0)
		{
			var r = orderedItems
				? ConsumeOrderedItems(recipe, items)
				: base.TryConsumeInputContents(recipe, items, none);
			if (!r.IsSuccess) return r;
		}
		if (fluids.Count > 0)
		{
			var r = orderedFluids
				? ConsumeOrderedFluids(recipe, fluids)
				: base.TryConsumeInputContents(recipe, none, fluids);
			if (!r.IsSuccess) return r;
		}
		return ActionResult.SUCCESS;
	}

	private bool CheckOrderedItems(IReadOnlyList<Content> items)
	{
		if (items.Count > _orderedItemBuses.Count) return false;
		for (int i = 0; i < items.Count; i++)
		{
			if (items[i].Payload is not Ingredient ing) return false;
			var bus = _orderedItemBuses[i];
			Item? firstStack = GetFirstNonEmptyStack(bus);
			if (firstStack == null || firstStack.IsAir) return false;
			if (!ing.Test(firstStack)) return false;
		}
		return true;
	}

	private bool CheckOrderedFluids(IReadOnlyList<Content> fluids)
	{
		if (fluids.Count > _orderedFluidHatches.Count) return false;
		for (int i = 0; i < fluids.Count; i++)
		{
			if (fluids[i].Payload is not FluidIngredient ing) return false;
			var hatch = _orderedFluidHatches[i];
			var firstStack = GetFirstNonEmptyFluid(hatch);
			if (!ing.TestStack(firstStack)) return false;
		}
		return true;
	}

	// Two-pass: simulate-all -> bail on partial -> real-drain-all.
	private ActionResult ConsumeOrderedItems(GTRecipe recipe, IReadOnlyList<Content> items)
	{
		if (items.Count > _orderedItemBuses.Count)
			return ActionResult.Fail(null, ItemRecipeCapability.CAP, IO.IN);

		for (int i = 0; i < items.Count; i++)
		{
			if (items[i].Payload is not Ingredient ing) return ActionResult.FAIL_NO_REASON;
			if (!SimulateBusConsume(_orderedItemBuses[i], recipe, ing))
				return ActionResult.Fail(null, ItemRecipeCapability.CAP, IO.IN);
		}
		for (int i = 0; i < items.Count; i++)
		{
			if (items[i].Payload is not Ingredient ing) return ActionResult.FAIL_NO_REASON;
			if (!RealBusConsume(_orderedItemBuses[i], recipe, ing))
				return ActionResult.FAIL_NO_REASON;
		}
		return ActionResult.SUCCESS;
	}

	private ActionResult ConsumeOrderedFluids(GTRecipe recipe, IReadOnlyList<Content> fluids)
	{
		if (fluids.Count > _orderedFluidHatches.Count)
			return ActionResult.Fail(null, FluidRecipeCapability.CAP, IO.IN);

		for (int i = 0; i < fluids.Count; i++)
		{
			if (fluids[i].Payload is not FluidIngredient ing) return ActionResult.FAIL_NO_REASON;
			if (!SimulateHatchConsume(_orderedFluidHatches[i], recipe, ing))
				return ActionResult.Fail(null, FluidRecipeCapability.CAP, IO.IN);
		}
		for (int i = 0; i < fluids.Count; i++)
		{
			if (fluids[i].Payload is not FluidIngredient ing) return ActionResult.FAIL_NO_REASON;
			if (!RealHatchConsume(_orderedFluidHatches[i], recipe, ing))
				return ActionResult.FAIL_NO_REASON;
		}
		return ActionResult.SUCCESS;
	}

	private static bool SimulateBusConsume(NotifiableItemStackHandler bus, GTRecipe recipe, Ingredient ing)
	{
		var left = new List<Ingredient> { ing };
		var remaining = bus.HandleRecipeInner(IO.IN, recipe, left, simulate: true);
		return remaining is null || remaining.Count == 0;
	}

	private static bool RealBusConsume(NotifiableItemStackHandler bus, GTRecipe recipe, Ingredient ing)
	{
		var left = new List<Ingredient> { ing };
		var remaining = bus.HandleRecipeInner(IO.IN, recipe, left, simulate: false);
		return remaining is null || remaining.Count == 0;
	}

	private static bool SimulateHatchConsume(NotifiableFluidTank hatch, GTRecipe recipe, FluidIngredient ing)
	{
		var left = new List<FluidIngredient> { ing };
		var remaining = hatch.HandleRecipeInner(IO.IN, recipe, left, simulate: true);
		return remaining is null || remaining.Count == 0;
	}

	private static bool RealHatchConsume(NotifiableFluidTank hatch, GTRecipe recipe, FluidIngredient ing)
	{
		var left = new List<FluidIngredient> { ing };
		var remaining = hatch.HandleRecipeInner(IO.IN, recipe, left, simulate: false);
		return remaining is null || remaining.Count == 0;
	}

	private static Item? GetFirstNonEmptyStack(NotifiableItemStackHandler bus)
	{
		for (int s = 0; s < bus.GetSlots(); s++)
		{
			var stack = bus.Storage.GetStackInSlot(s);
			if (stack != null && !stack.IsAir) return stack;
		}
		return null;
	}

	private static FluidStack GetFirstNonEmptyFluid(NotifiableFluidTank hatch)
	{
		for (int t = 0; t < hatch.GetTanks(); t++)
		{
			var stack = hatch.GetFluidInTank(t);
			if (!stack.IsEmpty) return stack;
		}
		return FluidStack.Empty;
	}
}
