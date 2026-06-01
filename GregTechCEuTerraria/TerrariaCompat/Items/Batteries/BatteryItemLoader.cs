#nullable enable
using System.Collections.Generic;
using System.Linq;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Items.Registry;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Batteries;

// Registers a BatteryItem per upstream battery, enumerated from the registry
// dump. A battery is a ComponentItem with ElectricStats.Dischargeable=true
// (electric TOOLS are chargeable-only and fall through). Stats come straight
// off ElectricStats - never hand-typed. Hulls land in RegistryItemLoader.
public static class BatteryItemLoader
{
	private const string ComponentItemClass = "com.gregtechceu.gtceu.api.item.ComponentItem";
	private const int MaxTier = (int)VoltageTier.MAX;

	public static void Register(Mod mod)
	{
		// Charge-animation probe: 8-frame strip item/<id>/{1..8}.png if frame 1
		// is bundled, else a flat <id>.png.
		var bundledFiles = mod.GetFileNames().ToHashSet(System.StringComparer.OrdinalIgnoreCase);

		int registered = 0;
		foreach (var e in RegistryDump.Entries)
		{
			if (e.Class != ComponentItemClass) continue;
			if (e.Electric is not { Dischargeable: true } es) continue;

			var tier = (VoltageTier)System.Math.Clamp(es.Tier, 0, MaxTier);
			bool animated = bundledFiles.Contains($"Content/Textures/item/{e.BareId}/1.rawimg");

			mod.AddContent(new BatteryItem(e.BareId, e.Name, tier, es.Capacity,
				es.Chargeable, es.Dischargeable, animated));
			registered++;
		}

		mod.Logger.Info($"BatteryItemLoader: registered {registered} batteries from the registry dump.");
	}
}
