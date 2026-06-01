#nullable enable
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Common.Machine.Trait;
using GregTechCEuTerraria.Config;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

namespace GregTechCEuTerraria.Common.Recipe.Condition;

// Port of com.gregtechceu.gtceu.common.recipe.condition.CleanroomCondition.
//
// Recipe requires the machine to host an active `CleanroomReceiverTrait`
// pointing at a provider that supplies the named CleanroomType. The provider
// today is the Cleaning Maintenance Hatch (bound to its controller); the full
// Cleanroom multiblock adds another. Test() mirrors upstream verbatim - see
// CleanroomCondition.java:53-64.
//
// Documented adaptations:
//   - Upstream stores a typed `CleanroomType` field; we keep the string id
//     (already round-tripped via `RecipeConditionJson`) and resolve through
//     `CleanroomType.GetByName` at Test time. Translation key lookups via
//     `CleanroomType.GetByName(...)?.TranslationKey` for tooltips.
//   - Upstream's `ConfigHolder.machines.{enableCleanroom,cleanMultiblocks}`
//     map to `GTConfig.EnableCleanroom` / `GTConfig.CleanMultiblocks`
//     (both default true, matching upstream defaults).
public sealed class CleanroomCondition : RecipeCondition
{
	public string CleanroomType { get; }

	public CleanroomCondition() : this("cleanroom") { }
	public CleanroomCondition(string cleanroomType) { CleanroomType = cleanroomType; }

	// Verbatim port of CleanroomCondition.java:53-64. Order, short-circuits
	// and the trailing `return true` (PASS on null receiver / null type) are
	// load-bearing - they make non-cleanroom-aware machines silently pass any
	// cleanroom-tagged recipe, matching upstream behavior.
	public override bool Test(RecipeLogic logic)
	{
		var machine = logic.Machine;

		if (!GTConfig.Instance.EnableCleanroom) return true;
		if (GTConfig.Instance.CleanMultiblocks
		    && machine is MultiblockControllerMachine) return true;

		var receiver = machine.Traits.GetTrait(CleanroomReceiverTrait.TYPE);
		var type = Api.Machine.Multiblock.CleanroomType.GetByName(CleanroomType);

		if (receiver != null && type != null) return receiver.HasActiveCleanroom(type);
		return true;
	}

	public override string GetTooltips() => $"Requires cleanroom: {CleanroomType}";
	public override string GetTypeName() => "gtceu:cleanroom";

	// Diagnostic: report the specific failure mode the player can act on.
	// DEVIATION from upstream (which only ships the generic
	// "Requires cleanroom" tooltip with no breakdown).
	public override string GetFailureMessage(RecipeLogic logic)
	{
		var machine = logic.Machine;
		var receiver = machine.Traits.GetTrait(CleanroomReceiverTrait.TYPE);
		if (receiver == null) return "Machine has no cleanroom receiver (internal)";
		if (receiver.CleanroomProvider == null) return "Not inside a formed cleanroom";

		var type = Api.Machine.Multiblock.CleanroomType.GetByName(CleanroomType);
		if (type == null) return $"Unknown cleanroom type: {CleanroomType}";

		var provider = receiver.CleanroomProvider;
		if (!provider.ProvidedTypes.Contains(type))
			return $"Cleanroom provides wrong type (need {type.Name})";
		if (!provider.IsActive)
			return "Cleanroom not clean yet (95% required)";
		return $"Requires cleanroom: {type.Name}";
	}
}
