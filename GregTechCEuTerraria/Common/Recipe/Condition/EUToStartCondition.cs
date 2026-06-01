#nullable enable
using GregTechCEuTerraria.Api.Machine.Feature;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.Common.Recipe.Condition;

// LOCKED - port of
// com.gregtechceu.gtceu.common.recipe.condition.EUToStartCondition.
//
// Recipe requires the machine's EU buffer to have at least `EUToStart` stored
// before the recipe begins. Drained ONCE at recipe start (not per tick).
//
// Used by recipes with high startup cost (research, fusion).
public sealed class EUToStartCondition : RecipeCondition
{
	public long EUToStart { get; }

	public EUToStartCondition() : this(0L) { }
	public EUToStartCondition(long euToStart) { EUToStart = euToStart; }

	public override bool Test(RecipeLogic logic)
	{
		// Resolve the machine's energy container via the trait holder. The
		// check is read-only - actual drain happens at recipe SetupRecipe
		// via the energy capability's handleRecipeIO call.
		// Cast to MetaMachine for trait-holder access; every concrete
		// IRecipeLogicMachine implementer is also a MetaMachine in
		// our tML port (no other root entity type carries traits).
		if (logic.GetRLMachine() is not MetaMachine mte) return true;
		var ec = mte.Traits.GetTrait(NotifiableEnergyContainer.TYPE);
		return ec is not null && ec.EnergyStored >= EUToStart;
	}

	public override string GetTooltips() => $"Requires {EUToStart:N0} EU stored to start";
	public override string GetTypeName() => "gtceu:eu_to_start";
}
