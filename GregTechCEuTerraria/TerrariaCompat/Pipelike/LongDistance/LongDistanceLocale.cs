#nullable enable
using Terraria.Localization;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.LongDistance;

// English fallbacks for the long-distance endpoint item tooltips. The universal
// TieredMachineItem hover path reads numbered `MachineTooltip.<id>_N` keys
// (MachineTooltipLookup). These endpoints are MachineDefinition rows, not custom
// ModItems, so there's no ModifyTooltips override to carry usage text - we register
// the lines at Mod.Load instead (port-locale.py would emit them, but needs runData).
public static class LongDistanceLocale
{
	public static void RegisterAll()
	{
		Register("long_distance_item_pipeline_endpoint",
			"Endpoint for a Long Distance Item Pipeline.",
			"Teleports items to a far-away partner endpoint.");
		Register("long_distance_fluid_pipeline_endpoint",
			"Endpoint for a Long Distance Fluid Pipeline.",
			"Teleports fluids to a far-away partner endpoint.");
	}

	private static void Register(string id, string summary, string transport)
	{
		string prefix = $"Mods.GregTechCEuTerraria.MachineTooltip.{id}";
		Language.GetOrRegister($"{prefix}_0", () => $"[c/AAFFAA:{summary}]");
		Language.GetOrRegister($"{prefix}_1", () => $"[c/AAFFAA:{transport}]");
		Language.GetOrRegister($"{prefix}_2", () =>
			"[c/AAAAAA:1. Connect to the end of a long-distance pipe run.]");
		Language.GetOrRegister($"{prefix}_3", () =>
			"[c/AAAAAA:2. Place a matching endpoint at the far end.]");
		Language.GetOrRegister($"{prefix}_4", () =>
			"[c/AAAAAA:3. Screwdriver one to Input, the other to Output.]");
		Language.GetOrRegister($"{prefix}_5", () =>
			"[c/888888:Feed the Input from any side; it exits into a chest next to the Output.]");
		Language.GetOrRegister($"{prefix}_6", () =>
			"[c/888888:Endpoints must be at least 50 blocks apart.]");
	}
}
