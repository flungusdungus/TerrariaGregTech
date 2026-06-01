#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Recipe;

namespace GregTechCEuTerraria.Api.Capability;

// Port of com.gregtechceu.gtceu.api.capability.IDataAccessHatch.
//
// Contract for a multiblock part that gates recipe access by research data -
// the assembly-line multi reads research stamps from its hatches, and only
// recipes whose `ResearchCondition` is satisfied by an attached stick can
// proceed. Creative variants pass everything.
//
// Documented adaptations: verbatim. `Collection<IDataAccessHatch>` recursion
// guard preserved; the `ModifyRecipe` default-method body is preserved.
public interface IDataAccessHatch
{
	// True if `recipe` is unlocked by data this hatch carries. Stateless
	// for creative hatches; recipe-set-driven for stick hatches.
	bool IsRecipeAvailable(GTRecipe recipe) =>
		IsRecipeAvailable(recipe, NewSeen());

	bool IsRecipeAvailable(GTRecipe recipe, ICollection<IDataAccessHatch> seen);

	// True for "creative" data hatches that unlock everything.
	bool IsCreative();

	// TerrariaCompat diagnostic aid (NO upstream parallel): count of research-data
	// entries reachable FROM this hatch, traversing the same chain
	// IsRecipeAvailable walks (a receiver hops through its adjacent optical pipe /
	// hatch; a transmitter sums its multi's data-access hatches when the multi is
	// working). Sums entries instead of matching one recipe. Creative hatch ->
	// int.MaxValue (unlocks everything). Used ONLY by the hover-tooltip diagnostic
	// (`OpticalDataHatchMachine.AppendTooltip`), never by recipe logic. Each impl
	// must add itself to `seen` first and skip already-seen hatches (cycle guard),
	// exactly like IsRecipeAvailable. Default 0 for impls that hold no research.
	int CountVisibleResearch(ICollection<IDataAccessHatch> seen) => 0;

	// Recipe modifier: passes everything for creative hatches; otherwise
	// permits only recipes that have research data registered. Mirrors
	// upstream's default-method body.
	GTRecipe? ModifyRecipe(GTRecipe recipe)
	{
		if (IsCreative()) return recipe;
		if (IsRecipeAvailable(recipe)) return recipe;
		return null;
	}

	private ICollection<IDataAccessHatch> NewSeen()
	{
		var s = new HashSet<IDataAccessHatch>();
		s.Add(this);
		return s;
	}
}
