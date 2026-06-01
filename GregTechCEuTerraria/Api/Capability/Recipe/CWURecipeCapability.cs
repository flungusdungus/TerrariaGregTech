#nullable enable
using GregTechCEuTerraria.Api.Recipe.Content;

namespace GregTechCEuTerraria.Api.Capability.Recipe;

// Port of com.gregtechceu.gtceu.api.capability.recipe.CWURecipeCapability.
//
// Computation Work Units (CWU/t) recipe capability - research recipes /
// HPCA-driven recipes encode their per-tick CWU draw using this. Each
// recipe content payload is a plain int (CWU/t requested).
//
// Documented adaptations:
//   - `SerializerInteger.INSTANCE` ctor arg dropped - same reason as
//     `EURecipeCapability` (our content payloads come from `IngredientJson`
//     already typed; the per-capability serializer registry isn't needed).
//   - `addXEIInfo` UI helper DROPPED - recipe info goes through our
//     `RecipeRowRenderer` instead.
//
// Preserved verbatim:
//   - Name "cwu", color 0xFFEEEE00.
//   - `CopyInner(int)` returns the int directly (immutable).
//   - `CopyWithModifier` applies the ContentModifier to the int value.
public sealed class CWURecipeCapability : RecipeCapability<int>
{
	public static readonly CWURecipeCapability CAP = new();

	private CWURecipeCapability() : base("cwu") { }

	public override int CopyInner(int content) => content;

	public override int CopyWithModifier(int content, ContentModifier modifier) =>
		modifier.Apply(content);
}
