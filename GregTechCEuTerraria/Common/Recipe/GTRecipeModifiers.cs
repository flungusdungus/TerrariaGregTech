#nullable enable
using System;
using GregTechCEuTerraria.Api.Machine.Feature;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Modifier;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

namespace GregTechCEuTerraria.Common.Recipe;

// PARTIAL - port of com.gregtechceu.gtceu.common.data.GTRecipeModifiers.
//
// Multiblock modifiers ported: hatchParallel (PARALLEL_HATCH), crackerOverclock,
// ebfOverclock, pyrolyseOvenOverclock, multiSmelterParallel. Still unported:
// batchMode (needs IBatchMachine UI toggle) and ENVIRONMENT_REQUIREMENT
// (medical-condition system).
//
// Documented adaptation: upstream wraps ELECTRIC_OVERCLOCK in Util.memoize so
// each OverclockingLogic maps to a single RecipeModifier instance. We just
// build the four OC_* shortcuts eagerly - same effect, no memoization cache.
public static class GTRecipeModifiers
{
	// Non-upstream: flat 10x duration cut for the Primitive Blast Furnace.
	// Upstream PBF runs at recipe duration verbatim (no modifier); this is a
	// project-local QoL - long primitive recipes are tedious in early-game
	// Terraria pacing. Pure speed (no parallel, no EU change).
	public static readonly RecipeModifier PRIMITIVE_BLAST_FURNACE_SPEEDUP =
		new RecipeModifier((machine, recipe) =>
			ModifierFunction.Builder().DurationMultiplier(0.1).Build());

	// Given an OverclockingLogic, creates a RecipeModifier for an
	// IOverclockMachine.
	public static readonly Func<OverclockingLogic, RecipeModifier> ELECTRIC_OVERCLOCK = logic =>
		new RecipeModifier((machine, recipe) =>
		{
			if (machine is not IOverclockMachine overclockMachine)
				return ModifierFunction.IDENTITY;
			if (RecipeHelper.GetRecipeEUtTier(recipe) > overclockMachine.MaxOverclockTier)
				return ModifierFunction.Cancel("gtceu.recipe_modifier.insufficient_voltage");
			return logic.GetModifier(machine, recipe, overclockMachine.OverclockVoltage);
		});

	// Shortcuts for common OC logics.
	public static readonly RecipeModifier OC_PERFECT =
		ELECTRIC_OVERCLOCK(OverclockingLogic.PERFECT_OVERCLOCK);
	public static readonly RecipeModifier OC_NON_PERFECT =
		ELECTRIC_OVERCLOCK(OverclockingLogic.NON_PERFECT_OVERCLOCK);
	public static readonly RecipeModifier OC_PERFECT_SUBTICK =
		ELECTRIC_OVERCLOCK(OverclockingLogic.PERFECT_OVERCLOCK_SUBTICK);
	public static readonly RecipeModifier OC_NON_PERFECT_SUBTICK =
		ELECTRIC_OVERCLOCK(OverclockingLogic.NON_PERFECT_OVERCLOCK_SUBTICK);

	// Verbatim port of upstream `GTRecipeModifiers.ebfOverclock`:
	//   1. Validate machine kind (must be CoilWorkable...).
	//   2. Compute machine heat = coil.Temperature + 100 x max(0, tier - MV).
	//   3. Cancel if recipe lacks `ebf_temp` OR machine heat < recipe heat.
	//   4. Cancel if recipe EU/t tier > machine tier (voltage gate).
	//   5. Apply EU/t discount (`getCoilEUtDiscount`) + heating-coil OC.
	public static readonly RecipeModifier EBF_OVERCLOCK =
		new RecipeModifier((machine, recipe) =>
		{
			if (machine is not CoilWorkableElectricMultiblockMachine coilMachine)
				return ModifierFunction.Cancel("gtceu.recipe_modifier.wrong_machine_type");

			int blastFurnaceTemperature = coilMachine.CoilType.Temperature
				+ 100 * Math.Max(0, coilMachine.MultiTier - (int)VoltageTier.MV);
			int recipeTemp = ReadDataInt(recipe.Data, "ebf_temp");
			bool hasTemp = recipe.Data?.ContainsKey("ebf_temp") ?? false;
			if (!hasTemp || recipeTemp > blastFurnaceTemperature)
				return ModifierFunction.Cancel("gtceu.recipe_modifier.coil_temperature_too_low");

			if (RecipeHelper.GetRecipeEUtTier(recipe) > coilMachine.MultiTier)
				return ModifierFunction.Cancel("gtceu.recipe_modifier.insufficient_voltage");

			var discount = ModifierFunction.Builder()
				.EutMultiplier(OverclockingLogic.GetCoilEUtDiscount(recipeTemp, blastFurnaceTemperature))
				.Build();

			// Heating-coil OC closure - uses recipeTemp / machineTemp to
			// derive perfect-OC count, then runs the standard parallel cycle.
			var ocLogic = OverclockingLogic.Create((p, v) =>
				OverclockingLogic.HeatingCoilOC(p, v, recipeTemp, blastFurnaceTemperature));
			var oc = ocLogic.GetModifier(machine, recipe, coilMachine.OverclockVoltage);
			return oc.Compose(discount);
		});

	// Verbatim port of upstream `GTRecipeModifiers.crackerOverclock` (line 139).
	//   1. Cancel if machine isn't a CoilWorkable.
	//   2. NULL (= search next recipe) if recipe EU/t tier > machine tier.
	//   3. NON_PERFECT_OVERCLOCK_SUBTICK overclock.
	//   4. Apply `1 - 0.1 x coilTier` EU/t discount when coilTier > 0.
	public static readonly RecipeModifier CRACKER_OVERCLOCK =
		new RecipeModifier((machine, recipe) =>
		{
			if (machine is not CoilWorkableElectricMultiblockMachine coilMachine)
				return ModifierFunction.Cancel("gtceu.recipe_modifier.wrong_machine_type");
			if (RecipeHelper.GetRecipeEUtTier(recipe) > coilMachine.MultiTier)
				return ModifierFunction.NULL;

			var oc = OverclockingLogic.NON_PERFECT_OVERCLOCK_SUBTICK
				.GetModifier(machine, recipe, coilMachine.OverclockVoltage);
			if (coilMachine.CoilType.Tier > 0)
			{
				var coilDiscount = ModifierFunction.Builder()
					.EutMultiplier(1.0 - coilMachine.CoilType.Tier * 0.1)
					.Build();
				oc = oc.AndThen(coilDiscount);
			}
			return oc;
		});

	// Verbatim port of upstream `GTRecipeModifiers.pyrolyseOvenOverclock` (line
	// 209). NON_PERFECT_OVERCLOCK_SUBTICK + duration multiplier:
	//   - tier 0 (Cupronickel) -> 4/3 (= 75% speed, slower)
	//   - tier >= 1            -> 2 / (tier + 1) (faster, e.g. 2/2=1.0 at tier 1)
	public static readonly RecipeModifier PYROLYSE_OVERCLOCK =
		new RecipeModifier((machine, recipe) =>
		{
			if (machine is not CoilWorkableElectricMultiblockMachine coilMachine)
				return ModifierFunction.Cancel("gtceu.recipe_modifier.wrong_machine_type");
			if (RecipeHelper.GetRecipeEUtTier(recipe) > coilMachine.MultiTier)
				return ModifierFunction.NULL;

			int tier = coilMachine.CoilType.Tier;
			double durationMultiplier = tier == 0 ? (4.0 / 3.0) : (2.0 / (tier + 1));
			var durationModifier = ModifierFunction.Builder()
				.DurationMultiplier(durationMultiplier)
				.Build();

			var oc = OverclockingLogic.NON_PERFECT_OVERCLOCK_SUBTICK
				.GetModifier(machine, recipe, coilMachine.OverclockVoltage);
			return oc.AndThen(durationModifier);
		});

	// Verbatim port of upstream `GTRecipeModifiers.multiSmelterParallel` (line
	// 244). Hand-rewrites duration + EUt against coil level + discount, then
	// applies NON_PERFECT_OVERCLOCK + parallel content multiplier:
	//   maxParallel = 32 x coilLevel
	//   parallels   = ParallelLogic.GetParallelAmount(machine, recipe, maxParallel)
	//   duration    = 128 x 2 x parallels / maxParallels   (= shorter at high coil)
	//   eut         = 4 x maxParallels / (8 x coilEnergyDiscount)
	// Then NON_PERFECT_OVERCLOCK on the rewritten recipe + multiply all
	// contents by `parallels`. The OC is captured at modifier-build time
	// against the post-rewrite recipe (so the tier-up logic uses the new EUt).
	public static readonly RecipeModifier MULTI_SMELTER_PARALLEL =
		new RecipeModifier((machine, recipe) =>
		{
			if (machine is not CoilWorkableElectricMultiblockMachine coilMachine)
				return ModifierFunction.Cancel("gtceu.recipe_modifier.wrong_machine_type");

			int maxParallel = 32 * coilMachine.CoilType.Level;
			int parallels   = Api.Recipe.Modifier.ParallelLogic.GetParallelAmount(
				(IRecipeLogicMachine)machine, recipe, maxParallel);
			if (parallels == 0) return ModifierFunction.NULL;

			int  duration = (int)(128 * 2.0 * parallels / maxParallel);
			long eut      = (long)(4 * maxParallel / (8.0 * coilMachine.CoilType.EnergyDiscount));

			var baseModifier = ModifierFunction.Of(r =>
			{
				var copy = r.Copy();
				Api.Capability.Recipe.EURecipeCapability.PutEUContent(
					copy.TickInputs, new Api.Recipe.Ingredient.EnergyStack(Math.Max(1, eut)));
				copy.Duration = Math.Max(1, duration);
				return copy;
			});

			var copyForOC = baseModifier.Apply(recipe);
			if (copyForOC is null) return ModifierFunction.NULL;
			var ocModifier = OverclockingLogic.NON_PERFECT_OVERCLOCK
				.GetModifier(machine, copyForOC, coilMachine.OverclockVoltage);
			var parallelModifier = ModifierFunction.Builder()
				.ModifyAllContents(Api.Recipe.Content.ContentModifier.Multiplier_(parallels))
				.Parallels(parallels)
				.Build();

			return baseModifier.AndThen(ocModifier).AndThen(parallelModifier);
		});

	// Verbatim port of upstream `GTRecipeModifiers.hatchParallel`
	// (GTRecipeModifiers.java:91-105). Reads `currentParallel` off the
	// controller's bound parallel hatch, runs `ParallelLogic` to find the
	// achievable count under the recipe's actual input/output room, then
	// returns a modifier that multiplies content x parallels and EU/t x
	// parallels.
	//
	//   - `controller.getParallelHatch()` (Optional) ->
	//     `controller.GetParallelHatch()` (nullable). orElse(1) -> `?? 1`.
	//   - `ContentModifier.multiplier(n)` -> `ContentModifier.Multiplier_(n)`
	//     (the trailing underscore avoids a static-class-name collision).
	public static readonly RecipeModifier PARALLEL_HATCH =
		new RecipeModifier((machine, recipe) =>
		{
			if (machine is not MultiblockControllerMachine controller || !controller.IsFormed)
				return ModifierFunction.IDENTITY;
			var hatch = controller.GetParallelHatch();
			int parallels = hatch == null
				? 1
				: Api.Recipe.Modifier.ParallelLogic.GetParallelAmount(
					(IRecipeLogicMachine)machine, recipe, hatch.CurrentParallel);
			if (parallels == 1) return ModifierFunction.IDENTITY;
			return ModifierFunction.Builder()
				.ModifyAllContents(Api.Recipe.Content.ContentModifier.Multiplier_(parallels))
				.EutMultiplier(parallels)
				.Parallels(parallels)
				.Build();
		});

	// PERFECT_HALF_DURATION_FACTOR (0.5) + PERFECT_HALF_VOLTAGE_FACTOR (2.0),
	// subtick = false - verbatim upstream `FusionReactorMachine.FUSION_OC`.
	// Declared BEFORE FUSION_OC so the nullable-flow analyzer sees a non-null
	// initialiser at the point of capture; functionally either order works
	// (lambda body runs long after static init).
	private static readonly OverclockingLogic _fusionOcLogic = OverclockingLogic.Create(
		OverclockingLogic.PERFECT_HALF_DURATION_FACTOR,
		OverclockingLogic.PERFECT_HALF_VOLTAGE_FACTOR,
		subtick: false);

	// Verbatim port of `FusionReactorMachine.recipeModifier` (line 166).
	//
	//   1. Cancel if recipe EU/t tier > reactor tier.
	//   2. Cancel if recipe has no `eu_to_start` or it exceeds capacitor capacity.
	//   3. heatDiff = eu_to_start - heat
	//      - heatDiff <= 0 -> already hot, apply FUSION_OC.
	//      - else if capacitor stores < heatDiff -> cancel.
	//      - else drain capacitor + add to heat -> apply FUSION_OC.
	//   4. FUSION_OC = PERFECT_HALF_DURATION (0.5) + PERFECT_HALF_VOLTAGE (2.0),
	//      subtick=false. Caps at reactor's GetMaxVoltage (which is min(V[tier],
	//      super.GetMaxVoltage())).
	//
	// SIDE-EFFECTING modifier: mutates `_heat` and the capacitor's EnergyStored
	// directly when accepting the recipe - verbatim with upstream. Mirrors
	// upstream's pattern of paying the startup cost up-front at modifier time,
	// not at first tick.
	public static readonly RecipeModifier FUSION_OC =
		new RecipeModifier((machine, recipe) =>
		{
			if (machine is not TerrariaCompat.Machine.Multiblock.Electric.FusionReactorMachine fusion)
				return ModifierFunction.Cancel("gtceu.recipe_modifier.wrong_machine_type");
			if (recipe is null)
				return ModifierFunction.Cancel("gtceu.recipe_modifier.wrong_machine_type");

			// Defensive: GTRecipe.Data is non-nullable per the ctor, but recipes
			// constructed via reflection / older serializer paths can leave the
			// field as default(TagCompound) which is null at runtime.
			//
			// `ReadDataLong` is type-tolerant: our JSON `ReadTag` prefers Int32
			// when the value fits (so `eu_to_start: 600000000` lands as int,
			// not long). tML's `TagCompound.Get<long>` is type-strict and
			// throws on int-stored values, which is why a direct `GetLong` NREs
			// for every fusion recipe. Walk the raw stored value instead.
			var data = recipe.Data;
			bool hasEuToStart = data?.ContainsKey("eu_to_start") ?? false;
			long euToStart   = ReadDataLong(data, "eu_to_start");

			// Split upstream's single `insufficient_eu_to_start_fusion` reason
			// into three player-actionable messages. Upstream collapses all three
			// to one key; we diverge in surface only (the gate logic is verbatim)
			// because "Recipe Modifier Fail" with no follow-up is unactionable -
			// the player has no way to tell whether the issue is the recipe tier,
			// the capacitor size, or just charge-up time. All three locale keys
			// live in `port-locale.py` `_RECIPE_STATUS`.
			if (Api.Recipe.RecipeHelper.GetRecipeEUtTier(recipe) > fusion.GetTier())
				return ModifierFunction.Cancel("gtceu.recipe_modifier.fusion_tier_too_low");
			if (!hasEuToStart)
				return ModifierFunction.Cancel("gtceu.recipe_modifier.insufficient_eu_to_start_fusion");
			if (euToStart > fusion.CapacitorContainer.EnergyCapacity)
				return ModifierFunction.Cancel("gtceu.recipe_modifier.fusion_capacity_too_small");

			long heatDiff = euToStart - fusion.Heat;

			// Already hot enough - recipe runs at zero startup cost.
			if (heatDiff <= 0)
				// Verbatim with upstream: pass `fusion.GetMaxVoltage()` (the
				// tier-capped value) and `shouldParallel: false` - Fusion does
				// NOT parallel, even though the perfect-half OC logic could.
				return _fusionOcLogic.GetModifier(machine, recipe, fusion.GetMaxVoltage(), shouldParallel: false);

			// Capacitor isn't fully charged yet - capacity is large enough but the
			// recharge from input hatches hasn't filled it. Distinct from
			// `fusion_capacity_too_small` which fires when the capacitor can NEVER
			// hold enough EU (player needs more hatches).
			if (fusion.CapacitorContainer.EnergyStored < heatDiff)
				return ModifierFunction.Cancel("gtceu.recipe_modifier.fusion_capacitor_charging");

			// Pay the startup cost - drain the capacitor, top up heat.
			fusion.CapacitorContainer.RemoveEnergy(heatDiff);
			fusion.Heat += heatDiff;
			fusion.UpdatePreHeatSubscription();
			// Same `GetMaxVoltage()` + `shouldParallel: false` as the heatDiff<=0 branch.
			return _fusionOcLogic.GetModifier(machine, recipe, fusion.GetMaxVoltage(), shouldParallel: false);
		});

	// Type-tolerant numeric accessors for `GTRecipe.Data` - our JSON `ReadTag`
	// (GTRecipeSerializer:269) stores integral values as `int` when they fit
	// in Int32, else `long`. tML's `TagCompound.Get<T>` is type-strict and
	// throws on mismatch, so a direct `data.GetLong("eu_to_start")` NREs when
	// the value landed as int (which is every fusion recipe - `eu_to_start`
	// maxes at 600M, fits in Int32). These helpers walk the raw stored value
	// and accept either width.
	public static long ReadDataLong(Terraria.ModLoader.IO.TagCompound? data, string key, long fallback = 0L)
	{
		if (data is null || !data.ContainsKey(key)) return fallback;
		object v = data[key];
		return v switch
		{
			long l   => l,
			int i    => i,
			short s  => s,
			byte b   => b,
			_        => fallback,
		};
	}

	public static int ReadDataInt(Terraria.ModLoader.IO.TagCompound? data, string key, int fallback = 0)
	{
		if (data is null || !data.ContainsKey(key)) return fallback;
		object v = data[key];
		return v switch
		{
			int i    => i,
			long l   => (int)l,
			short s  => s,
			byte b   => b,
			_        => fallback,
		};
	}
}
