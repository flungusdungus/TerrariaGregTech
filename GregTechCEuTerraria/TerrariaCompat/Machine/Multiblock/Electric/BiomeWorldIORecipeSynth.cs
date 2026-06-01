#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Category;
using GregTechCEuTerraria.Common.Recipe.Condition;
using GregTechCEuTerraria.Api.Recipe.Chance.Logic;
using GregTechCEuTerraria.Api.Recipe.Content;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.Common.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Items;
using GregTechCEuTerraria.TerrariaCompat.Recipes;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Electric;

// Synthetic GTRecipes for the world-I/O multis so the recipe browser shows
// one row per biome per station. NEVER executed - LargeMiner / FluidDrillingRig
// override IsRecipeLogicAvailable() to false. Pure browser discoverability.
// Called from Mod.Load AFTER RecipeJsonLoader.Load.
public static class BiomeWorldIORecipeSynth
{
	public static void Register(Mod mod)
	{
		var per = new Dictionary<string, List<GTRecipe>>();
		int miner = 0, rig = 0;

		var minerList = new List<GTRecipe>();
		foreach (var (biome, pool) in BiomeWorldIOTables.Ores)
		{
			var recipe = BuildMinerRecipe(biome, pool);
			if (recipe is null) continue;
			minerList.Add(recipe);
			miner++;
		}
		if (minerList.Count > 0) per[GTRecipeTypes.LARGE_MINER.RegistryName] = minerList;

		var rigList = new List<GTRecipe>();
		foreach (BiomeProbe.Biome biome in System.Enum.GetValues(typeof(BiomeProbe.Biome)))
		{
			var recipe = BuildRigRecipe(biome);
			if (recipe is null) continue;
			rigList.Add(recipe);
			rig++;
		}
		if (rigList.Count > 0) per[GTRecipeTypes.FLUID_DRILLING_RIG.RegistryName] = rigList;

		RecipeRegistry.AppendAll(per);
		mod.Logger.Info(
			$"BiomeWorldIORecipeSynth: synthesized {miner} large_miner + {rig} fluid_drilling_rig browser recipes.");
	}

	private static GTRecipe? BuildMinerRecipe(BiomeProbe.Biome biome, BiomeWorldIOTables.OreDrop[] pool)
	{
		// Shared resolver matches in-world drops (raw_ore -> vanilla -> gem -> dust).
		var resolved = new List<(int ItemType, int Weight)>();
		int totalWeight = 0;
		foreach (var entry in pool)
		{
			int type = BiomeWorldIOTables.ResolveOreItem(entry.MaterialId);
			if (type <= 0) continue;
			resolved.Add((type, entry.Weight));
			totalWeight += entry.Weight;
		}
		if (resolved.Count == 0 || totalWeight == 0) return null;

		var outputs = new Dictionary<object, List<Content>>();
		var outList = new List<Content>(resolved.Count);
		foreach (var (itemType, weight) in resolved)
			outList.Add(new Content(new ItemStackIngredient(itemType), weight, totalWeight, 0));
		outputs[ItemRecipeCapability.CAP] = outList;

		var inputs = new Dictionary<object, List<Content>>();
		var drillingFluid = FluidRegistry.Get("drilling_fluid");
		if (drillingFluid is not null)
		{
			inputs[FluidRecipeCapability.CAP] = new List<Content>
			{
				new(new FluidIngredient(drillingFluid, 4),
					ChanceLogic.GetMaxChancedValue(), ChanceLogic.GetMaxChancedValue(), 0),
			};
		}

		return BuildRecipe(
			GTRecipeTypes.LARGE_MINER,
			id:         $"large_miner/{biome.ToString().ToLowerInvariant()}",
			inputs:     inputs,
			outputs:    outputs,
			eutInput:   VoltageTiers.V((int)VoltageTier.EV),
			duration:   200,
			biome:      biome);
	}

	private static GTRecipe? BuildRigRecipe(BiomeProbe.Biome biome)
	{
		var fluid = BiomeWorldIOTables.GetFluid(biome);
		if (fluid is null) return null;

		var outputs = new Dictionary<object, List<Content>>();
		outputs[FluidRecipeCapability.CAP] = new List<Content>
		{
			new(new FluidIngredient(fluid, 1),
				ChanceLogic.GetMaxChancedValue(), ChanceLogic.GetMaxChancedValue(), 0),
		};

		return BuildRecipe(
			GTRecipeTypes.FLUID_DRILLING_RIG,
			id:         $"fluid_drilling_rig/{biome.ToString().ToLowerInvariant()}",
			inputs:     new Dictionary<object, List<Content>>(),
			outputs:    outputs,
			eutInput:   VoltageTiers.V((int)VoltageTier.MV),
			duration:   20,
			biome:      biome);
	}

	// Mirrors NativeRecipeProxy.BuildSynthetic shape; local because of different
	// defaults (no category, browser-only).
	private static GTRecipe BuildRecipe(GTRecipeType type, string id,
		Dictionary<object, List<Content>> inputs,
		Dictionary<object, List<Content>> outputs,
		long eutInput, int duration, BiomeProbe.Biome biome)
	{
		var tickInputs = new Dictionary<object, List<Content>>();
		EURecipeCapability.PutEUContent(tickInputs, new EnergyStack(eutInput, 1));

		// Label-only - controller's OnTick runs its own scan; Test() never reached.
		var conditions = new List<RecipeCondition>
		{
			new BiomeCondition(biome.ToString()),
		};

		return new GTRecipe(
			recipeType:              type,
			id:                      id,
			inputs:                  inputs,
			outputs:                 outputs,
			tickInputs:              tickInputs,
			tickOutputs:             new Dictionary<object, List<Content>>(),
			inputChanceLogics:       new Dictionary<object, ChanceLogic>(),
			outputChanceLogics:      new Dictionary<object, ChanceLogic>(),
			tickInputChanceLogics:   new Dictionary<object, ChanceLogic>(),
			tickOutputChanceLogics:  new Dictionary<object, ChanceLogic>(),
			conditions:              conditions,
			ingredientActions:       System.Array.Empty<object>(),
			data:                    new TagCompound(),
			duration:                duration,
			recipeCategory:          GTRecipeCategory.DEFAULT,
			groupColor:              -1);
	}
}
