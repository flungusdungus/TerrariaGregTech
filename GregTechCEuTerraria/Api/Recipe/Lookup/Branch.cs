#nullable enable
using System.Collections.Generic;

namespace GregTechCEuTerraria.Api.Recipe.Lookup;

// PORTED - verbatim port of
// com.gregtechceu.gtceu.api.recipe.lookup.Branch.
//
// One node of the recipe-lookup trie. Each edge is keyed by an
// AbstractMapIngredient and leads to Either a leaf recipe or a deeper Branch.
//
//   nodes        - keys with (should-be) unique hashcodes; fast hash lookup.
//   specialNodes - keys whose hashes collide deliberately; differentiated by
//                  equality. AbstractMapIngredient.IsSpecialIngredient() picks
//                  which map an ingredient goes into.
//
// Both maps are lazily allocated - most branches only ever use `nodes`.
// upstream's Object2ObjectOpenHashMap -> C# Dictionary (AbstractMapIngredient
// overrides GetHashCode/Equals, so it keys correctly).
internal sealed class Branch
{
	private Dictionary<AbstractMapIngredient, Either<GTRecipe, Branch>>? _nodes;
	private Dictionary<AbstractMapIngredient, Either<GTRecipe, Branch>>? _specialNodes;

	public bool IsEmptyBranch() =>
		(_nodes == null        || _nodes.Count == 0) &&
		(_specialNodes == null || _specialNodes.Count == 0);

	public Dictionary<AbstractMapIngredient, Either<GTRecipe, Branch>> GetNodes() =>
		_nodes ??= new Dictionary<AbstractMapIngredient, Either<GTRecipe, Branch>>(2);

	public Dictionary<AbstractMapIngredient, Either<GTRecipe, Branch>> GetSpecialNodes() =>
		_specialNodes ??= new Dictionary<AbstractMapIngredient, Either<GTRecipe, Branch>>(2);

	// Removes all nodes in the branch.
	public void Clear()
	{
		_specialNodes = null;
		_nodes        = null;
	}
}
