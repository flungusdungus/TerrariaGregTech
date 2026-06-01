#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Recipe.Condition;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.Common.Recipe.Condition;

// LOCKED - Terraria-adapted port of
// com.gregtechceu.gtceu.common.recipe.condition.DimensionCondition.
//
// Recipe runs only in the specified MC dimension. Terraria has no
// dimension concept - instead we map MC's 3 vanilla dimensions to the
// MACHINE'S altitude zone (NOT the player's zone - recipes evaluate
// per-machine, not per-player; in MP the player might be elsewhere or
// non-existent server-side).
//
//   minecraft:overworld  -> machine in overworld surface band
//                          (between space and underworld)
//   minecraft:the_nether -> machine in underworld zone (Hell - bottom of
//                          world, lava lake)
//   minecraft:the_end    -> machine in space zone (top of world, low
//                          gravity, starry background) - the "extreme
//                          altitude alien zone" analogue
//   gtceu:dim_<X>        -> no mapping; returns true (recipe runs unconstrained)
//
// Modded dimensions (Twilight Forest, Aether, etc.) have no Terraria
// equivalent and default to true.
public sealed class DimensionCondition : RecipeCondition
{
	public string DimensionId { get; }

	public DimensionCondition() : this("") { }
	public DimensionCondition(string dimensionId) { DimensionId = dimensionId; }

	public override bool Test(RecipeLogic logic)
	{
		if (logic.GetRLMachine() is not MetaMachine mte) return true;
		int y = mte.Position.Y;
		return DimensionId switch
		{
			"minecraft:overworld"  => LocationZone.IsOverworld(y),
			"minecraft:the_nether" => LocationZone.IsUnderworld(y),
			"minecraft:the_end"    => LocationZone.IsSpace(y),
			_ => true,
		};
	}

	public override string GetTooltips() => $"Requires dimension: {DimensionId}";
	public override string GetTypeName() => "gtceu:dimension";
}
