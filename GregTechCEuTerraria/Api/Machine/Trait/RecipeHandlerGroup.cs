#nullable enable
namespace GregTechCEuTerraria.Api.Machine.Trait;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.machine.trait.RecipeHandlerGroup.
//
// Discriminator for batching distinct-vs-grouped handler lists. Each
// RecipeHandlerList carries a RecipeHandlerGroup field. Two RHLs with the
// same group are recipe-matched together (pooled); different groups stay
// separate. Implementations:
//   - RecipeHandlerGroupColor   : color-keyed (cover/dye system)
//   - RecipeHandlerGroupDistinctness : BUS_DISTINCT (per-slot match) or
//     BYPASS_DISTINCT (always indistinct regardless of group)
//
// Identity is by Equals/GetHashCode - UNDYED handlers share one canonical
// instance; distinct buses produce singletons.
public interface RecipeHandlerGroup
{
	// Object.Equals and Object.GetHashCode are already implicitly required;
	// upstream re-declares them in the interface for emphasis.
}
