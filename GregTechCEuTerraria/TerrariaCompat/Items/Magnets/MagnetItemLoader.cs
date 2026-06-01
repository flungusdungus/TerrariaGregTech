#nullable enable
using System;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Items.Registry;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Magnets;

// Registers a MagnetItem per ComponentItem dump entry whose id ends
// `_item_magnet`. Tier + capacity from ElectricStats; range hard-coded per
// GTItems.java (not in dump). MUST run before RegistryItemLoader.
public static class MagnetItemLoader
{
	private const string ComponentItemClass = "com.gregtechceu.gtceu.api.item.ComponentItem";

	// Verbatim GTItems.java (ITEM_MAGNET_LV / _HV).
	private static int RangeFor(string bareId) => bareId switch
	{
		"lv_item_magnet" => 8,
		"hv_item_magnet" => 32,
		_                => 0,
	};

	public static void Register(Mod mod)
	{
		int registered = 0;
		foreach (var e in RegistryDump.Entries)
		{
			if (e.Class != ComponentItemClass) continue;
			if (!e.BareId.EndsWith("_item_magnet", StringComparison.Ordinal)) continue;

			int range = RangeFor(e.BareId);
			if (range == 0)
			{
				mod.Logger.Warn($"MagnetItemLoader: no range mapping for {e.Id} - skipped.");
				continue;
			}
			if (e.Electric is not { } es) continue;

			var tier = (VoltageTier)Math.Clamp(es.Tier, 0, (int)VoltageTier.MAX);
			mod.AddContent(new MagnetItem(e.BareId, e.Name, tier, es.Capacity, range));
			registered++;
		}

		mod.Logger.Info($"MagnetItemLoader: registered {registered} item magnets from the registry dump.");
	}
}
