#nullable enable
using System.Collections.Generic;

namespace GregTechCEuTerraria.Api.Cover;

// Adaptation of GTRegistries.COVERS - a flat id -> CoverDefinition registry.
// Upstream uses a Mojang registry; we keep a static dictionary, matching the
// pattern already used for RecipeCapability. Cover content is registered into
// it by GTCovers at mod load.
public static class CoverRegistry
{
	private static readonly Dictionary<string, CoverDefinition> _byId = new();

	public static CoverDefinition Register(CoverDefinition definition)
	{
		_byId[definition.Id] = definition;
		return definition;
	}

	public static CoverDefinition? Get(string id) =>
		_byId.TryGetValue(id, out var def) ? def : null;

	public static IReadOnlyCollection<CoverDefinition> All => _byId.Values;

	public static void Clear() => _byId.Clear();
}
