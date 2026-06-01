#nullable enable
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;
using Terraria;

namespace GregTechCEuTerraria.Common.Recipe.Condition;

// LOCKED - Terraria-adapted port of
// com.gregtechceu.gtceu.common.recipe.condition.ThunderCondition.
//
// Recipe runs only during a thunderstorm of at least `level` intensity.
// Terraria has no native thunderstorm; we map to slime rain (Main.slimeRain)
// as the closest analogue - atypical-weather gating. Documented as a
// behavioral adaptation; if a more faithful equivalent is added later
// (Calamity-mod-style storms, blood moon weather), replace the mapping.
public sealed class ThunderCondition : RecipeCondition
{
	public float Level { get; }

	public ThunderCondition() : this(0f) { }
	public ThunderCondition(float level) { Level = level; }

	public override bool Test(RecipeLogic logic) =>
		Main.slimeRain && (Main.maxRaining >= Level || Level <= 0f);

	public override string GetTooltips() => $"Requires thunder (intensity >= {Level:F2})";
	public override string GetTypeName() => "gtceu:thunder";
}
