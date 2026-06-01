#nullable enable
using GregTechCEuTerraria.Api.Machine.Trait;

namespace GregTechCEuTerraria.Api.Recipe;

// PARTIAL - port of com.gregtechceu.gtceu.api.recipe.RecipeCondition.
//
// Runtime predicate on a recipe - extra checks beyond capability matching:
// is it raining? Is the machine in a cleanroom? Is it a certain biome?
// Conditions are evaluated by RecipeLogic.checkConditions before SetupRecipe
// runs, and again every tick if `isOrChain()` returns true.
//
// Upstream is a generic `RecipeCondition<T extends RecipeCondition<?>>` with
// codec serialization, tooltip rendering, and `setReverse` / `isReverse`
// inversion logic. We port the math surface here; codec + UI deferred.
public abstract class RecipeCondition
{
	public bool IsReverse { get; protected set; }

	// Verbatim port of upstream's `test(RecipeLogic)`. Returns true if the
	// condition holds; if IsReverse is set, the result is inverted by
	// RecipeLogic.checkConditions.
	public abstract bool Test(RecipeLogic logic);

	public RecipeCondition SetReverse(bool reverse) { IsReverse = reverse; return this; }

	// Verbatim port of `isOrChain()` - if true, the condition is checked
	// every tick (not just at recipe start). Default false.
	public virtual bool IsOrChain() => false;

	// Verbatim port of `getTooltips()` - i18n key for the condition's
	// failure reason. Default empty. Subclasses override.
	public virtual string GetTooltips() => "";

	// Specific failure diagnostic - returned when `Test(logic)` is false. The
	// default falls back to GetTooltips() (the static "requires X" text).
	// Subclasses with multiple failure modes (CleanroomCondition: not bound /
	// not clean / wrong type / no receiver) override to return the actual
	// reason. DEVIATION from upstream - upstream emits a single
	// generic "conditions not met" with no breakdown; ours surfaces the
	// specific failure so the player can diagnose without digging in code.
	public virtual string GetFailureMessage(RecipeLogic logic) => GetTooltips();

	// Verbatim port of `getType()` identity. Used by the recipe-condition
	// registry (deferred - flat type-name string for now).
	public abstract string GetTypeName();
}
