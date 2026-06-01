#nullable enable
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;

namespace GregTechCEuTerraria.Common.Recipe.Condition;

// PARTIAL - port of
// com.gregtechceu.gtceu.common.recipe.condition.VentCondition.
//
// Recipe requires the machine to have a clear vent on the back face - used
// by Steam Turbines / generators that exhaust steam. Requires the multiblock
// vent-side-check + facing logic (machine rotation + per-side blocking
// check) that we haven't ported.
//
// Stub returns true; recipes that need vent-checking will run without the
// gate until the vent system is ported.
public sealed class VentCondition : RecipeCondition
{
	public VentCondition() { }

	public override bool Test(RecipeLogic logic) => true;

	public override string GetTooltips() => "Requires clear vent";
	public override string GetTypeName() => "gtceu:vent";
}
