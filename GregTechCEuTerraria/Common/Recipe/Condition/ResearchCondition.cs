#nullable enable
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;

namespace GregTechCEuTerraria.Common.Recipe.Condition;

// PARTIAL - port of
// com.gregtechceu.gtceu.common.recipe.condition.ResearchCondition.
//
// Recipe requires a research project to be completed before it unlocks.
// Used by Assembly Line recipes - the player must have built the research
// data and run it through a Scanner machine.
//
// The research system isn't ported yet (Phase 6+). Stub returns true so
// research-gated recipes are accessible without research; this is a known
// approximation until the research system lands.
public sealed class ResearchCondition : RecipeCondition
{
	public string ResearchId { get; }

	public ResearchCondition() : this("") { }
	public ResearchCondition(string researchId) { ResearchId = researchId; }

	public override bool Test(RecipeLogic logic) => true;  // research system not ported

	public override string GetTooltips() => $"Requires research: {ResearchId}";
	public override string GetTypeName() => "gtceu:research";
}
