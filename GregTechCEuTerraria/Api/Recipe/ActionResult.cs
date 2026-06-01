#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;

namespace GregTechCEuTerraria.Api.Recipe;

// LOCKED - verbatim port of com.gregtechceu.gtceu.api.recipe.ActionResult.
// DO NOT modify behavior; mirror upstream changes only.
//
// Result-type returned by recipe-handler operations: match, consume, produce.
// Carries optional reason + the capability+io that produced the failure (so
// UI / tooltip can surface "this recipe failed because of EU shortage in slot
// X").
//
// Documented adaptations:
//   - Component -> string (Terraria has no Component; tooltip strings render
//     plain). The empty-component default becomes the empty string.
//   - RecipeCapability<?> wildcard -> non-generic `object?` (matches our
//     `MachineTraitType` adaptation pattern - capability is passed as an
//     opaque token for failure reporting).
public sealed record ActionResult(
	bool IsSuccess,
	string? Reason = null,
	object? Capability = null,
	IO? Io = null)
{
	public static readonly ActionResult SUCCESS              = new(true,  null,                                       null, null);
	public static readonly ActionResult FAIL_NO_REASON       = new(false, null,                                       null, null);
	public static readonly ActionResult PASS_NO_CONTENTS     = new(true,  "gtceu.recipe_logic.no_contents",           null, null);
	public static readonly ActionResult FAIL_NO_CAPABILITIES = new(false, "gtceu.recipe_logic.no_capabilities",       null, null);

	public static ActionResult Fail(string? reason, object? capability, IO io) =>
		new(false, reason, capability, io);

	// Mirrors upstream's `reason()` override that maps null -> empty.
	public string ReasonText() => Reason ?? string.Empty;
}
