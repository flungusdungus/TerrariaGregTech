#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using GregTechCEuTerraria.Api.Pattern.Error;
using Terraria;
using Terraria.Localization;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

// TerrariaCompat-side beautification of upstream-locked Api PatternError text.
// Collapses tier-prefixed candidates so "ULV Output Bus +14 more" becomes
// "Output Bus (ULV~MAX)" - communicates "any tier works", less misleading.
public static class MultiblockErrorText
{
	private static readonly string[] TierShort =
	{
		"ULV", "LV", "MV", "HV", "EV", "IV", "LuV", "ZPM",
		"UV",  "UHV","UEV","UIV","UXV","OpV","MAX",
	};

	public static string Describe(PatternError err) =>
		err is SinglePredicateError spe ? DescribeCount(spe) : err.ErrorInfo;

	// Count errors -> single line (avoid duplicating status). Wrong-block errors
	// keep the base PatternError's per-candidate bullet list.
	public static IEnumerable<string> DescribeLines(PatternError err)
	{
		if (err is SinglePredicateError spe)
		{
			yield return DescribeCount(spe);
			yield break;
		}
		foreach (var line in err.ErrorDetailLines())
			yield return line;
	}

	private static string DescribeCount(SinglePredicateError spe)
	{
		// Type: 0 too-many global, 1 not-enough global, 2 too-many/layer, 3 not-enough/layer.
		bool tooMany  = spe.Type == 0 || spe.Type == 2;
		bool perLayer = spe.Type == 2 || spe.Type == 3;
		int required = spe.Type switch
		{
			0 => spe.Predicate.MaxCount,
			1 => spe.Predicate.MinCount,
			2 => spe.Predicate.MaxLayerCount,
			3 => spe.Predicate.MinLayerCount,
			_ => -1,
		};

		// Per-layer count is wiped between layers; only global is reliable.
		int actual = -1;
		if (!perLayer && spe.WorldState is not null)
			spe.WorldState.GlobalCount.TryGetValue(spe.Predicate, out actual);

		string desc     = DescribeCandidates(spe.Predicate.GetCandidates());
		string verb     = tooMany ? "Too many" : "Not enough";
		string layer    = perLayer ? " per layer" : "";
		string needWord = tooMany ? "max" : "need";
		string have     = actual >= 0 ? $", have {actual}" : "";
		return $"{verb} {desc}{layer} {needWord} {required}{have}";
	}

	// "<base> (<low>~<high>)" when every entry shares a tier-prefix; else a
	// short "/"-joined list.
	private static string DescribeCandidates(List<Item> candidates)
	{
		var bases    = new List<string>();
		var seenBase = new HashSet<string>(StringComparer.Ordinal);
		int minTier = int.MaxValue, maxTier = -1;

		foreach (var it in candidates)
		{
			if (it is null || it.IsAir) continue;
			(int tier, string baseName) = SplitTier(Lang.GetItemName(it.type).Value);
			if (tier >= 0)
			{
				if (tier < minTier) minTier = tier;
				if (tier > maxTier) maxTier = tier;
			}
			if (seenBase.Add(baseName)) bases.Add(baseName);
		}

		if (bases.Count == 0) return "(unknown)";

		if (bases.Count == 1)
		{
			if (maxTier < 0) return bases[0];
			string range = minTier == maxTier
				? TierShort[minTier]
				: $"{TierShort[minTier]}~{TierShort[maxTier]}";
			return $"{bases[0]} ({range})";
		}

		var sb = new StringBuilder();
		int show = Math.Min(2, bases.Count);
		for (int i = 0; i < show; i++)
		{
			if (i > 0) sb.Append(" / ");
			sb.Append(bases[i]);
		}
		if (bases.Count > show) sb.Append($" +{bases.Count - show} more");
		return sb.ToString();
	}

	// tierIndex < 0 when the leading token isn't a voltage short-name.
	private static (int tier, string baseName) SplitTier(string name)
	{
		int sp = name.IndexOf(' ');
		if (sp <= 0) return (-1, name);
		int idx = Array.IndexOf(TierShort, name.Substring(0, sp));
		return idx < 0 ? (-1, name) : (idx, name.Substring(sp + 1));
	}
}
