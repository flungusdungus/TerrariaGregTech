#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Common.Energy;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Single source of machine identity. Populated by MachineDefinitions.RegisterAll
// at Mod.Load. Lookups: by id ("macerator") + by tile type (backstop for
// pre-save-blob identity recovery).
public static class MachineRegistry
{
	private static readonly Dictionary<string, MachineDefinition> _byId = new();
	private static readonly Dictionary<int, (string Id, VoltageTier Tier)> _byTileType = new();

	public static void Register(MachineDefinition def) => _byId[def.Id] = def;

	public static MachineDefinition Get(string id) => _byId[id];

	public static bool TryGet(string id, out MachineDefinition def) =>
		_byId.TryGetValue(id, out def!);

	public static IEnumerable<MachineDefinition> All => _byId.Values;

	public static int Count => _byId.Count;

	// Called by TieredMachineFactory once tile.Type is assigned.
	public static void MapTile(int tileType, string id, VoltageTier tier) =>
		_byTileType[tileType] = (id, tier);

	public static bool TryResolveTile(int tileType, out string id, out VoltageTier tier)
	{
		if (_byTileType.TryGetValue(tileType, out var v))
		{
			id = v.Id;
			tier = v.Tier;
			return true;
		}
		id = "";
		tier = default;
		return false;
	}

	// Used by Predicates.Machines(...) in the multiblock matcher.
	public static IEnumerable<int> TilesForId(string id)
	{
		foreach (var kv in _byTileType)
			if (kv.Value.Id == id) yield return kv.Key;
	}

	// Reload hygiene - tML keeps statics across a mod reload.
	internal static void Clear()
	{
		_byId.Clear();
		_byTileType.Clear();
	}
}
