#nullable enable
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.Common.Energy;

namespace GregTechCEuTerraria.Api.Recipe;

// PARTIAL - port of com.gregtechceu.gtceu.api.recipe.RecipeHelper.
//
// Upstream's matchContents / matchTickRecipe / handleRecipeIO / trimRecipeOutputs
// were collapsed into the IRecipeLogicMachine machine surface
// (handleRecipeIO collapsed into IRecipeLogicMachine.
// {TryMatchInputContents, ...}). MatchRecipe + EU-tier helpers wrap that
// surface for the recipe-modifier chain.
public static class RecipeHelper
{
	// Verbatim port of upstream `RecipeHelper.matchRecipe(machine, recipe)`.
	// Routes through `IRecipeLogicMachine.TryMatchInputContents` - the same
	// flat hook RecipeLogic.MatchRecipe uses. Mirrors upstream's
	// `matchContents(machine, recipe, IO.IN)` minus the TickInputs check
	// (callers building a synthetic recipe like the combustion engine's
	// lubricant recipe have no tick inputs).
	public static ActionResult MatchRecipe(
		Api.Machine.Feature.IRecipeLogicMachine machine, GTRecipe recipe)
	{
		var itemIn  = recipe.GetInputContents(Api.Capability.Recipe.ItemRecipeCapability.CAP);
		var fluidIn = recipe.GetInputContents(Api.Capability.Recipe.FluidRecipeCapability.CAP);
		return machine.TryMatchInputContents(recipe, itemIn, fluidIn);
	}

	// Verbatim port of upstream `RecipeHelper.handleRecipeIO(machine, recipe,
	// io, chanceCaches)`. Routes to the machine's IO.IN consume hook or IO.OUT
	// deposit hook (the same flat hooks RecipeLogic.HandleRecipeIO uses).
	// `chanceCaches` arg upstream threads through to ChanceLogic.roll for
	// chanced outputs; we don't surface that yet on the flat hooks (chanced
	// roll happens inside the trait's HandleRecipeInner per-content), so the
	// arg is accepted-but-unused. Callers can pass null today.
	public static ActionResult HandleRecipeIO(
		Api.Machine.Feature.IRecipeLogicMachine machine,
		GTRecipe recipe,
		Api.Capability.Recipe.IO io,
		System.Collections.Generic.IDictionary<string, int>? chanceCache = null)
	{
		var items  = io == Api.Capability.Recipe.IO.IN
			? recipe.GetInputContents (Api.Capability.Recipe.ItemRecipeCapability.CAP)
			: recipe.GetOutputContents(Api.Capability.Recipe.ItemRecipeCapability.CAP);
		var fluids = io == Api.Capability.Recipe.IO.IN
			? recipe.GetInputContents (Api.Capability.Recipe.FluidRecipeCapability.CAP)
			: recipe.GetOutputContents(Api.Capability.Recipe.FluidRecipeCapability.CAP);
		return io == Api.Capability.Recipe.IO.IN
			? machine.TryConsumeInputContents(recipe, items, fluids)
			: machine.DepositOutputContents(recipe, items, fluids, machine.GetRecipeLogic());
	}

	// === Unified recipe-IO entry (Phase 1 of the recipe-IO surface parity) ===
	// Port of upstream `RecipeHelper.handleRecipe(holder, recipe, io, contents,
	// chanceCaches, isTick, simulate)`. The recipe ALREADY stores its contents as
	// capability-keyed maps (`Inputs` / `Outputs` / `TickInputs` / `TickOutputs`,
	// each with ITEM / FLUID / EU / CWU entries), so this just selects the right
	// map for (io, isTick) and dispatches the WHOLE map through the holder's single
	// `HandleRecipe` walk - one call, all capabilities together (vs the legacy
	// per-cap fan-out). Behaviour is identical while the transitional shim
	// (`IRecipeLogicMachine.HandleRecipe` default) re-splits to the legacy hooks;
	// multiblocks override `HandleRecipe` to run the real group-aware dispatch.
	public static ActionResult HandleRecipe(
		Api.Machine.Feature.IRecipeLogicMachine machine,
		GTRecipe recipe, Api.Capability.Recipe.IO io, bool isTick, bool simulate)
	{
		var contents = io == Api.Capability.Recipe.IO.IN
			? (isTick ? recipe.TickInputs  : recipe.Inputs)
			: (isTick ? recipe.TickOutputs : recipe.Outputs);
		return machine.HandleRecipe(recipe, io, contents, isTick, simulate, machine.GetRecipeLogic());
	}

	// Port of upstream `RecipeHelper.matchContents(holder, recipe)` =
	// `matchRecipe(IO.IN) && matchRecipe(IO.OUT room) && matchTickRecipe(IO.IN)`.
	// All three are simulate-only (no mutation). First failing branch wins, so
	// capability+io failure attribution is preserved. Identical to the legacy
	// MatchRecipe (regular-IN match -> regular-OUT room -> tick-IN feasibility).
	public static ActionResult MatchContents(
		Api.Machine.Feature.IRecipeLogicMachine machine, GTRecipe recipe)
	{
		var inR = HandleRecipe(machine, recipe, Api.Capability.Recipe.IO.IN,  isTick: false, simulate: true);
		if (!inR.IsSuccess) return inR;
		var outR = HandleRecipe(machine, recipe, Api.Capability.Recipe.IO.OUT, isTick: false, simulate: true);
		if (!outR.IsSuccess) return outR;
		return HandleRecipe(machine, recipe, Api.Capability.Recipe.IO.IN, isTick: true, simulate: true);
	}


	// Verbatim port of getRealEUt - inputEUt if non-empty, else outputEUt.
	public static EnergyStack GetRealEUt(GTRecipe recipe)
	{
		var stack = recipe.InputEUt;
		if (!stack.IsEmpty()) return stack;
		return recipe.OutputEUt;
	}

	// Verbatim port of getRealEUtWithIO. Upstream returns an EnergyStack.WithIO
	// record; we return a (stack, isInput) tuple - EnergyStack.WithIO itself
	// is not ported (Codec/network-only otherwise).
	public static (EnergyStack Stack, bool IsInput) GetRealEUtWithIO(GTRecipe recipe)
	{
		var stack = recipe.InputEUt;
		if (!stack.IsEmpty()) return (stack, true);
		return (recipe.OutputEUt, false);
	}

	// Verbatim port of getRecipeEUtTier.
	public static int GetRecipeEUtTier(GTRecipe recipe)
	{
		var stack = GetRealEUt(recipe);
		long eut = stack.Voltage;
		if (recipe.Parallels > 1) eut /= recipe.Parallels;
		return VoltageTiers.TierByVoltage(eut);
	}

	// Verbatim port of getPreOCRecipeEuTier - the recipe's base EU/t tier before
	// any overclock shifts have been applied. Used by chance-boost calculations
	// (display lines + chanced-output rolls), where `chanceTier = preOCTier +
	// recipe.ocLevel`.
	public static int GetPreOCRecipeEuTier(GTRecipe recipe)
	{
		var stack = GetRealEUt(recipe);
		long eut = stack.GetTotalEU();
		if (recipe.Parallels > 1) eut /= recipe.Parallels;
		eut >>= recipe.OcLevel * 2;
		return VoltageTiers.TierByVoltage(eut);
	}
}
