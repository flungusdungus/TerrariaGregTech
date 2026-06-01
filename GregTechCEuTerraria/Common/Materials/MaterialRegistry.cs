#nullable enable
using GregTechCEuTerraria.Api.Data.Chemical.Material;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Fluids.Store;
using GregTechCEuTerraria.Api.Fluids;
using System.Collections.Generic;

namespace GregTechCEuTerraria.Common.Materials;

public static class MaterialRegistry
{
	private static readonly Dictionary<string, Material> _byId = new();

	public static IReadOnlyDictionary<string, Material> All => _byId;

	public static void Register(Material material)
	{
		_byId[material.Id] = material;
	}

	public static Material? Get(string id) => _byId.GetValueOrDefault(id);

	internal static void Clear() => _byId.Clear();
}
