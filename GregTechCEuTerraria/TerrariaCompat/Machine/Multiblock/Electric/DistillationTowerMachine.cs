#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine.Feature.Multiblock;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Misc;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Content;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Electric;

// Port of DistillationTowerMachine. distillation_tower (per-layer fluid output)
// + large_distillery (same column + distillery recipes). layer i -> hatch at
// layer i; missing layers void (upstream VoidFluidHandler parity). distillery
// recipes fall through to base. Layer = (controllerY - hatchY) / 2 - 1.
public sealed class DistillationTowerMachine : WorkableElectricMultiblockMachine
{
	protected override string Label => Definition?.Label ?? "Distillation Tower";

	// Missing layers = void sink (upstream parity).
	private readonly Dictionary<int, NotifiableFluidTank> _layerHatches = new();

	public DistillationTowerMachine() : base() { }

	public override void OnStructureFormed()
	{
		base.OnStructureFormed();
		RebuildLayerHatchMap();
	}

	public override void OnStructureInvalid()
	{
		base.OnStructureInvalid();
		_layerHatches.Clear();
	}

	public override void OnPartUnload()
	{
		base.OnPartUnload();
		_layerHatches.Clear();
	}

	private void RebuildLayerHatchMap()
	{
		_layerHatches.Clear();
		int controllerY = Position.Y;
		foreach (var part in GetParts())
		{
			if (part is not FluidHatchPartMachine fh) continue;
			if (fh.Io != IO.OUT) continue;
			if (fh.Tank is null) continue;

			int diff = controllerY - fh.Position.Y;
			if (diff <= 0 || (diff & 1) != 0) continue;
			int layer = diff / 2 - 1;
			if (layer < 0) continue;
			// Pattern's setMaxLayerLimited(1) handles uniqueness; safety last-write.
			_layerHatches[layer] = fh.Tank;
		}
	}

	// Tower recipes route per-layer; distillery recipes fall through to base.
	private static bool IsTowerRecipe(GTRecipe recipe) =>
		recipe.RecipeType.RegistryName == "distillation_tower";

	public override ActionResult HasOutputRoomContents(
		GTRecipe recipe,
		IReadOnlyList<Content> items,
		IReadOnlyList<Content> fluids)
	{
		if (!IsTowerRecipe(recipe))
			return base.HasOutputRoomContents(recipe, items, fluids);

		// Items + EU base; fluids per-layer.
		var itemResult = base.HasOutputRoomContents(recipe, items, System.Array.Empty<Content>());
		if (!itemResult.IsSuccess) return itemResult;
		return SimulateFluidsPerLayer(fluids);
	}

	public override ActionResult DepositOutputContents(
		GTRecipe recipe,
		IReadOnlyList<Content> items,
		IReadOnlyList<Content> fluids,
		RecipeLogic logic)
	{
		if (!IsTowerRecipe(recipe))
			return base.DepositOutputContents(recipe, items, fluids, logic);

		var itemResult = base.DepositOutputContents(recipe, items, System.Array.Empty<Content>(), logic);
		if (!itemResult.IsSuccess) return itemResult;
		return DepositFluidsPerLayer(fluids);
	}

	private ActionResult SimulateFluidsPerLayer(IReadOnlyList<Content> fluids)
	{
		for (int i = 0; i < fluids.Count; i++)
		{
			var stack = ResolveOutputStack(fluids[i]);
			if (stack.IsEmpty) continue;
			if (!_layerHatches.TryGetValue(i, out var tank))
				continue; // void layer (upstream parity)
			int accepted = tank.FillInternal(stack, simulate: true);
			if (accepted < stack.Amount)
				return ActionResult.Fail(null, FluidRecipeCapability.CAP, IO.OUT);
		}
		return ActionResult.SUCCESS;
	}

	private ActionResult DepositFluidsPerLayer(IReadOnlyList<Content> fluids)
	{
		for (int i = 0; i < fluids.Count; i++)
		{
			var stack = ResolveOutputStack(fluids[i]);
			if (stack.IsEmpty) continue;
			if (!_layerHatches.TryGetValue(i, out var tank))
				continue; // void
			tank.FillInternal(stack, simulate: false);
		}
		return ActionResult.SUCCESS;
	}

	// Output FluidIngredient is exact-type; multi-fluid splits across Content entries.
	private static FluidStack ResolveOutputStack(Content content)
	{
		if (content.Payload is not FluidIngredient fi || fi.IsEmpty)
			return FluidStack.Empty;
		var stacks = fi.GetStacks();
		return stacks.Length == 0 ? FluidStack.Empty : stacks[0];
	}
}
