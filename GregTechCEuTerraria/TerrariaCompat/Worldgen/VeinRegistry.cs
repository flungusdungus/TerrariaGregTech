#nullable enable
using System.Collections.Generic;

namespace GregTechCEuTerraria.TerrariaCompat.Worldgen;

public static class VeinRegistry
{
	private static readonly List<VeinDefinition> _veins = new();

	public static IReadOnlyList<VeinDefinition> All => _veins;
	public static int Count => _veins.Count;

	public static void Register(VeinDefinition v) => _veins.Add(v);
	public static void Clear() => _veins.Clear();
}
