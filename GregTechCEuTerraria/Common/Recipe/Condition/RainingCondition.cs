#nullable enable
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;
using Terraria;

namespace GregTechCEuTerraria.Common.Recipe.Condition;

// LOCKED - Terraria-adapted port of
// com.gregtechceu.gtceu.common.recipe.condition.RainingCondition.
//
// Recipe runs only when it's raining at least `level` intensity.
// Adaptations:
//   - MC's `level.getRainLevel(1)` returns a 0..1 float interpolation of
//     rain intensity.
//   - Terraria's `Main.maxRaining` is the equivalent 0..1 rain intensity.
//   - We test against Main.raining flag + Main.maxRaining intensity.
public sealed class RainingCondition : RecipeCondition
{
	public float Level { get; }

	public RainingCondition() : this(0f) { }
	public RainingCondition(float level) { Level = level; }

	public override bool Test(RecipeLogic logic) =>
		Main.raining && Main.maxRaining >= Level;

	public override string GetTooltips() => $"Requires rain (intensity >= {Level:F2})";
	public override string GetTypeName() => "gtceu:rain";
}
