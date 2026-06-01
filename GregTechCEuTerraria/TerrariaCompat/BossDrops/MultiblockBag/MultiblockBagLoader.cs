#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.BossDrops.MultiblockBag;

// Registers one MultiblockBagItem per multi (every MachineDefinition with a
// non-null PatternFactory). MUST run AFTER MachineDefinitions.RegisterAll +
// TieredMachineFactory.RegisterAll so the controller items exist by the time
// MultiblockBagContents.Resolve walks them.
public static class MultiblockBagLoader
{
	public const string NamePrefix = "multiblock_bag_";

	// multi id -> bag item type
	private static readonly Dictionary<string, int> _byMultiId = new();
	public static IReadOnlyDictionary<string, int> ByMultiId => _byMultiId;

	public static bool TryGet(string multiId, out int itemType) =>
		_byMultiId.TryGetValue(multiId, out itemType);

	public static IEnumerable<KeyValuePair<string, int>> All => _byMultiId;

	public static void Register(Mod mod)
	{
		_byMultiId.Clear();
		int registered = 0;
		foreach (var def in MachineRegistry.All)
		{
			if (def.PatternFactory is null) continue;
			var bag = new MultiblockBagItem(def.Id, def.Label);
			mod.AddContent(bag);
			_byMultiId[def.Id] = bag.Type;
			registered++;
		}
		mod.Logger.Info($"MultiblockBagLoader: registered {registered} multiblock bags.");
	}

	public static void Unload() => _byMultiId.Clear();
}
