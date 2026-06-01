#nullable enable
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;
using Terraria;

namespace GregTechCEuTerraria.Common.Recipe.Condition;

// LOCKED - Terraria-adapted port of
// com.gregtechceu.gtceu.common.recipe.condition.PositionYCondition.
//
// Recipe runs only when the machine's Y position is within [Min, Max] tile
// coordinates. Used by upstream for depth-gated processing (e.g. deep-ore
// processing requires you to be deep underground).
//
// Documented adaptation:
//   - MC Y grows upward; Terraria Y grows DOWNWARD. The min/max semantics
//     therefore invert visually: low Y = high altitude in Terraria. We use
//     raw tile-Y coordinates from the machine's Position.Y; recipe authors
//     should pick numbers matching Terraria's coordinate convention
//     (Main.worldSurface is around y=Main.maxTilesY/2 typically).
public sealed class PositionYCondition : RecipeCondition
{
	public int MinY { get; }
	public int MaxY { get; }

	public PositionYCondition() : this(int.MinValue, int.MaxValue) { }
	public PositionYCondition(int minY, int maxY) { MinY = minY; MaxY = maxY; }

	public override bool Test(RecipeLogic logic)
	{
		int y = logic.Machine.Position.Y;
		return y >= MinY && y <= MaxY;
	}

	public override string GetTooltips() => $"Y position in [{MinY}..{MaxY}]";
	public override string GetTypeName() => "gtceu:pos_y";
}
