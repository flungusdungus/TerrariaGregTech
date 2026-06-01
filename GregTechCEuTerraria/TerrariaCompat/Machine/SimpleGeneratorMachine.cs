#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Content;
using GregTechCEuTerraria.Api.Recipe.Modifier;
using GregTechCEuTerraria.Common.Energy;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// 1:1 port of SimpleGeneratorMachine. All singleblock generators - extends
// WorkableTieredMachine and drives EU production through RecipeLogic
// (recipes with non-zero OutputEUt). HandleTickRecipeIO(IO.OUT) dispatches
// OutputEUt to DepositOutputEU (NEC emitter mode).
// Dropped: hazardEmitter, tintColor / EditableMachineUI.
public class SimpleGeneratorMachine : WorkableTieredMachine
{
	public SimpleGeneratorMachine() { }
	public SimpleGeneratorMachine(VoltageTier tier) : base(tier) { }

	// Upstream isEnergyEmitter() -> true. TieredEnergyMachine picks emitter
	// mode from CanAccept/CanExtract.
	public override bool CanAccept  => false;
	public override bool CanExtract => true;

	// SimpleGeneratorMachine has no charger slot (only SimpleTieredMachine does).
	protected override bool HasChargerSlot => false;

	public override bool RegressWhenWaiting() => false;

	// Verbatim SimpleGeneratorMachine.recipeModifier - fast-parallelize up to
	// (overclockVoltage / recipeOutputEUt) times, no voltage overclock.
	public static readonly RecipeModifier Modifier = new((machine, recipe) =>
	{
		if (machine is not SimpleGeneratorMachine generator)
			return RecipeModifier.NullWrongType();
		long EUt = recipe.OutputEUt.GetTotalEU();
		if (EUt <= 0) return ModifierFunction.NULL;

		int maxParallel = (int)(generator.OverclockVoltage / EUt);
		int parallels = ParallelLogic.GetParallelAmountFast(generator, recipe, maxParallel);

		return ModifierFunction.Builder()
			.InputModifier(ContentModifier.Multiplier_(parallels))
			.OutputModifier(ContentModifier.Multiplier_(parallels))
			.EutMultiplier(parallels)
			.Parallels(parallels)
			.Build();
	});

	public override RecipeModifier GetRecipeModifier() => Modifier;
}
