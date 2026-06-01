#nullable enable
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;

namespace GregTechCEuTerraria.Common.Recipe.Condition;

// PARTIAL - port of
// com.gregtechceu.gtceu.common.recipe.condition.EnvironmentalHazardCondition.
//
// Recipe checks for the presence of an environmental hazard (radiation,
// chemical) in the area. The environmental-hazard system isn't ported.
//
// Stub returns true; recipes that gate on hazard presence will run without
// the check.
public sealed class EnvironmentalHazardCondition : RecipeCondition
{
	public string HazardType { get; }

	public EnvironmentalHazardCondition() : this("") { }
	public EnvironmentalHazardCondition(string hazardType) { HazardType = hazardType; }

	public override bool Test(RecipeLogic logic) => true;

	public override string GetTooltips() => $"Requires environmental hazard: {HazardType}";
	public override string GetTypeName() => "gtceu:environmental_hazard";
}
