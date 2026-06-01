#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine.Feature;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Category;
using GregTechCEuTerraria.Api.Recipe.Chance.Logic;
using GregTechCEuTerraria.Api.Recipe.Content;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.Api.Recipe.Modifier;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.Common.Recipe;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Generator;

// Port of LargeCombustionEngineMachine. combustion_generator recipes at tier
// voltage. 1 mB lubricant/72t; oxygen -> x1.5 EU @ 2x voltage (or LO2 4 mB/t
// -> x2.0 EXTREME). isIntakesObstructed dropped (no facing). D-cell OUTPUT_ENERGY
// tier filter dropped (single-amp emitters self-cap).
public sealed class LargeCombustionEngineMachine : WorkableElectricMultiblockMachine
{
	private bool _isOxygenBoosted;
	private int  _runningTimer;

	public bool IsOxygenBoosted => _isOxygenBoosted;

	public LargeCombustionEngineMachine() : base() { }

	private int TierIdx => (int)Tier;

	public bool IsExtreme() => TierIdx > (int)VoltageTier.EV;

	// Boost = V[tier+1]; output hatches must handle one tier higher.
	public bool IsBoostAllowed() => GetMaxVoltage() >= VoltageTiers.V(TierIdx + 1);

	public override bool RegressWhenWaiting() => false;

	// Tier-driven, not hatch-driven.
	public override long OverclockVoltage =>
		_isOxygenBoosted ? VoltageTiers.V(TierIdx) * 2 : VoltageTiers.V(TierIdx);

	public override RecipeModifier GetRecipeModifier() => CombustionModifier;

	private const string ReasonNoLubricant = "gtceu.recipe_modifier.no_lubricant";

	// Upstream LUBRICANT_STACK / OXYGEN_STACK / LIQUID_OXYGEN_STACK.
	private static GTRecipe? _lubricantRecipeCache;
	private static GTRecipe? _oxygenRecipeCache;
	private static GTRecipe? _liquidOxygenRecipeCache;
	private static GTRecipe GetLubricantRecipe() =>
		_lubricantRecipeCache ??= BuildSyntheticInputFluidRecipe("synthetic/lubricant", "lubricant", 1);
	private static GTRecipe GetOxygenRecipe() =>
		_oxygenRecipeCache ??= BuildSyntheticInputFluidRecipe("synthetic/oxygen", "oxygen", 1);
	private static GTRecipe GetLiquidOxygenRecipe() =>
		_liquidOxygenRecipeCache ??= BuildSyntheticInputFluidRecipe("synthetic/liquid_oxygen", "liquid_oxygen", 4);

	private GTRecipe GetBoostRecipe() => IsExtreme() ? GetLiquidOxygenRecipe() : GetOxygenRecipe();

	// = GTRecipeBuilder.ofRaw().inputFluids(stack).buildRawRecipe().
	private static GTRecipe BuildSyntheticInputFluidRecipe(string id, string fluidId, int amount)
	{
		var fluid = FluidRegistry.Get(fluidId)
			?? throw new System.InvalidOperationException($"{fluidId} fluid not registered");
		var inputs = new Dictionary<object, List<Content>>
		{
			[FluidRecipeCapability.CAP] = new List<Content>
			{
				new(new FluidIngredient(fluid, amount),
				    ChanceLogic.GetMaxChancedValue(), ChanceLogic.GetMaxChancedValue(), 0),
			},
		};
		return new GTRecipe(
			recipeType: GTRecipeTypes.DUMMY, id: id,
			inputs:                 inputs,
			outputs:                new Dictionary<object, List<Content>>(),
			tickInputs:             new Dictionary<object, List<Content>>(),
			tickOutputs:            new Dictionary<object, List<Content>>(),
			inputChanceLogics:      new Dictionary<object, ChanceLogic>(),
			outputChanceLogics:     new Dictionary<object, ChanceLogic>(),
			tickInputChanceLogics:  new Dictionary<object, ChanceLogic>(),
			tickOutputChanceLogics: new Dictionary<object, ChanceLogic>(),
			conditions:             new List<RecipeCondition>(),
			ingredientActions:      System.Array.Empty<object>(),
			data:                   new TagCompound(),
			duration:               1,
			recipeCategory:         GTRecipeCategory.DEFAULT,
			groupColor:             -1);
	}

	private static readonly RecipeModifier CombustionModifier = new((machine, recipe) =>
	{
		if (machine is not LargeCombustionEngineMachine engineMachine) return ModifierFunction.NULL;

		EnergyStack EUt = recipe.OutputEUt;
		// Sans isIntakesObstructed. Cancel surfaces "Out of lubricant" vs upstream NULL.
		if (!EUt.IsEmpty() && RecipeHelper.MatchRecipe(engineMachine, GetLubricantRecipe()).IsSuccess)
		{
			int maxParallel = (int)(engineMachine.OverclockVoltage / EUt.GetTotalEU());
			int actualParallel = ParallelLogic.GetParallelAmount(engineMachine, recipe, maxParallel);
			double eutMultiplier = actualParallel * engineMachine.GetProductionBoost();

			return ModifierFunction.Builder()
				.InputModifier(ContentModifier.Multiplier_(actualParallel))
				.OutputModifier(ContentModifier.Multiplier_(actualParallel))
				.EutMultiplier(eutMultiplier)
				.Parallels(actualParallel)
				.Build();
		}
		return EUt.IsEmpty()
			? ModifierFunction.NULL
			: ModifierFunction.Cancel(ReasonNoLubricant);
	});

	private double GetProductionBoost()
	{
		if (!_isOxygenBoosted) return 1.0;
		return IsExtreme() ? 2.0 : 1.5;
	}

	// Verbatim upstream OnWorking.
	public override bool OnWorking()
	{
		if (!base.OnWorking()) return false;

		if (_runningTimer % 72 == 0)
		{
			if (!RecipeHelper.HandleRecipeIO(this, GetLubricantRecipe(), IO.IN).IsSuccess)
			{
				Recipe.InterruptRecipe();
				return false;
			}
		}

		if (IsBoostAllowed())
		{
			var boosterRecipe = GetBoostRecipe();
			bool wasBoosted = _isOxygenBoosted;
			_isOxygenBoosted = RecipeHelper.MatchRecipe(this, boosterRecipe).IsSuccess
				&& RecipeHelper.HandleRecipeIO(this, boosterRecipe, IO.IN).IsSuccess;
			if (wasBoosted != _isOxygenBoosted && IsServer)
				TerrariaCompat.Net.MachineStateSyncPacket.Broadcast(this);
		}

		_runningTimer++;
		if (_runningTimer > 72000) _runningTimer %= 72000;
		return true;
	}

	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["lce_boost"] = _isOxygenBoosted;
		tag["lce_timer"] = _runningTimer;
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		if (tag.ContainsKey("lce_boost")) _isOxygenBoosted = tag.GetBool("lce_boost");
		if (tag.ContainsKey("lce_timer")) _runningTimer    = tag.GetInt("lce_timer");
	}

	public override void NetSend(System.IO.BinaryWriter writer)
	{
		base.NetSend(writer);
		writer.Write(_isOxygenBoosted);
	}

	public override void NetReceive(System.IO.BinaryReader reader)
	{
		base.NetReceive(reader);
		_isOxygenBoosted = reader.ReadBoolean();
	}
}
