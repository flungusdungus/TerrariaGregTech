#nullable enable
namespace GregTechCEuTerraria.Api.Machine.Trait;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.machine.trait.RecipeHandlerGroupColor.
//
// Color-keyed grouping for cover-dyed handler lists. Two handler lists with
// the same color value group together for recipe matching; different colors
// stay separate. Used by paint/dye covers.
//
// UNDYED = -1 is the sentinel for "no color" / "indistinct". Equivalent to
// an indistinct hatch behaviorally.
public sealed record RecipeHandlerGroupColor(int Color) : RecipeHandlerGroup
{
	public static readonly RecipeHandlerGroup UNDYED = new RecipeHandlerGroupColor(-1);
}
