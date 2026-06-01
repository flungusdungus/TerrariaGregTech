#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using GregTechCEuTerraria.TerrariaCompat.Items.Registry;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Covers;

// Registers a CoverItem per dump entry carrying a `cover` field. Items whose
// texture isn't bundled are skipped + logged (bundled-file probe pattern).
public static class CoverItemLoader
{
	// upstream id (e.g. "gtceu:lv_solar_panel") -> Terraria ItemType.
	private static readonly Dictionary<string, int> _byUpstreamId = new();
	public static IReadOnlyDictionary<string, int> ByUpstreamId => _byUpstreamId;

	public static bool TryGet(string upstreamId, out int itemType) =>
		_byUpstreamId.TryGetValue(upstreamId, out itemType);

	public static void Register(Mod mod)
	{
		_byUpstreamId.Clear();
		var bundled = mod.GetFileNames().ToHashSet(StringComparer.OrdinalIgnoreCase);

		int registered = 0, skipped = 0;
		foreach (var e in RegistryDump.Entries)
		{
			if (e.Cover is null) continue;
			// Defer to any dedicated registry that already owns this id.
			if (mod.TryFind<ModItem>(e.BareId, out _)) continue;
			// Skip cover items whose texture isn't bundled yet.
			if (!bundled.Contains($"Content/Textures/item/{e.BareId}.rawimg"))
			{
				skipped++;
				continue;
			}

			var item = new CoverItem(e.BareId, e.Name, e.MaxStack, e.Rarity, e.Cover);
			mod.AddContent(item);
			_byUpstreamId[e.Id] = item.Type;
			registered++;
		}

		mod.Logger.Info($"CoverItemLoader: registered {registered} cover items " +
		                $"({skipped} skipped - no bundled texture).");
	}

	public static void Unload() => _byUpstreamId.Clear();
}
