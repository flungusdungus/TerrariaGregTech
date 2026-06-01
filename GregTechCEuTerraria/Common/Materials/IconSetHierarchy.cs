#nullable enable
using System;
using System.Collections.Generic;

namespace GregTechCEuTerraria.Common.Materials;

// Upstream MaterialIconSet inheritance chain. When a (iconSet, prefix) pair
// has no bundled texture, fall through to the parent iconSet - e.g. RUBY's
// `ring.png` doesn't exist, so it resolves to EMERALD -> DIAMOND -> SHINY ->
// METALLIC where ring.png is defined.
//
// Mirrors the `parent` arg in upstream `new MaterialIconSet("name", PARENT)`
// declarations from MaterialIconSet.java.
internal static class IconSetHierarchy
{
	// Verbatim mirror of `MaterialIconSet.java` - DULL is the sole root; every
	// other set chains to it (METALLIC -> DULL, FINE -> DULL, ...).
	private static readonly Dictionary<string, string?> Parents = new(StringComparer.OrdinalIgnoreCase)
	{
		["DULL"]           = null,        // root
		["METALLIC"]       = "DULL",
		["MAGNETIC"]       = "METALLIC",
		["SHINY"]          = "METALLIC",
		["BRIGHT"]         = "SHINY",
		["DIAMOND"]        = "SHINY",
		["EMERALD"]        = "DIAMOND",
		["GEM_HORIZONTAL"] = "EMERALD",
		["GEM_VERTICAL"]   = "EMERALD",
		["RUBY"]           = "EMERALD",
		["OPAL"]           = "RUBY",
		["GLASS"]          = "RUBY",
		["NETHERSTAR"]     = "GLASS",
		["FINE"]           = "DULL",
		["SAND"]           = "FINE",
		["WOOD"]           = "FINE",
		["ROUGH"]          = "FINE",
		["FLINT"]          = "ROUGH",
		["LIGNITE"]        = "ROUGH",
		["QUARTZ"]         = "ROUGH",
		["CERTUS"]         = "QUARTZ",
		["LAPIS"]          = "QUARTZ",
		["RADIOACTIVE"]    = "METALLIC",
		["FLUID"]          = "DULL",
	};

	// Walks self -> parent -> grandparent..., terminating at DULL (the root of
	// every chain, and the fallback for an unknown/empty set - upstream
	// `MaterialInfo` defaults `iconSet` to DULL). Deduplicates so a stray
	// cycle can't loop.
	public static IEnumerable<string> WalkChain(string? iconSet)
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string? cur = string.IsNullOrEmpty(iconSet) ? "DULL" : iconSet;
		while (cur != null && seen.Add(cur))
		{
			yield return cur;
			Parents.TryGetValue(cur, out cur);
		}
		if (seen.Add("DULL")) yield return "DULL";
	}
}
