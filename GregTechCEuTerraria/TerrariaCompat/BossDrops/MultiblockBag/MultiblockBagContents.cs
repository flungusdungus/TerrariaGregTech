#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Pattern;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.BossDrops.MultiblockBag;

// Walks a multi's BlockPattern (largest preview size) into a (itemType -> count)
// drop list + one controller item. SimplePredicate-identity tracked throughout
// (not item type) so per-predicate caps are respected.
//
//   Pass 1 - per cell, take the FIRST chain SimplePredicate with candidates (the
//     PreviewItem primary), tally cells per primary. Skips Any/Air/Controller.
//   Cap pass - cap each primary's count at its MaxCount (SetExactLimit /
//     SetMaxGlobalLimited). Needed for Large Turbine: `R` occurs twice in `RHSR`
//     but the rotor-holder's SetExactLimit(1) -> 1 holder + 1 dynamo (dynamo via
//     Pass 2); without the cap the bag had 2 holders and no dynamo.
//   Pass 2 - sweep SimplePredicates never used as a primary; add 1 of each's
//     first candidate (deduped by type). Catches `.Or(...)` alternatives the cell
//     walk misses (steam hatch / dynamo / muffler behind a casing-first chain).
//
// Dedup is per-predicate AND per-type, so an ability on every wall cell yields one entry.
public static class MultiblockBagContents
{
	public readonly record struct Drop(int ItemType, int Count);

	// Resolve the bag contents for one multi. Returns null if the multi has no
	// PatternFactory or its controller item couldn't be located.
	public static List<Drop>? Resolve(Mod mod, MachineDefinition def)
	{
		if (def.PatternFactory is null) return null;
		var pattern = def.PatternFactory().GetPreviewPattern();

		// Controller item shares its tile's Name (tiered convention - multis only).
		var tier = def.Tiers.Length > 0 ? def.Tiers[0] : VoltageTier.LV;
		string controllerName = def.Tiered ? $"{VoltageTiers.Id(tier)}_{def.Id}" : def.Id;
		int controllerItemType = 0;
		if (mod.TryFind<ModItem>(controllerName, out var ctrlItem))
			controllerItemType = ctrlItem.Type;

		var counts = new Dictionary<int, int>();
		if (controllerItemType > 0) counts[controllerItemType] = 1;

		// Pass 1 (see header).
		var primaryCellCount = new Dictionary<SimplePredicate, int>();
		var primaryFor       = new Dictionary<TraceabilityPredicate, SimplePredicate?>();
		var seenPredicates   = new HashSet<TraceabilityPredicate>();

		for (int row = 0; row < pattern.Height; row++)
		{
			for (int col = 0; col < pattern.Width; col++)
			{
				char ch = pattern.Shape[row][col];
				if (!pattern.Predicates.TryGetValue(ch, out var predicate)) continue;
				if (predicate.IsController) continue;       // added once above
				if (predicate.IsAny() || predicate.IsAir()) continue;

				seenPredicates.Add(predicate);
				if (!primaryFor.TryGetValue(predicate, out var primary))
				{
					primary = FindPrimary(predicate);
					primaryFor[predicate] = primary;
				}
				if (primary is null) continue;

				primaryCellCount.TryGetValue(primary, out int existing);
				primaryCellCount[primary] = existing + 1;
			}
		}

		// Cap pass - convert (primary SP, cell count) into (item type, capped count).
		// Cap = MaxCount if set, else uncapped (use raw cell count).
		foreach (var kv in primaryCellCount)
		{
			var sp = kv.Key;
			int rawCount = kv.Value;
			int cap = sp.MaxCount > 0 ? sp.MaxCount : rawCount;
			int count = System.Math.Min(rawCount, cap);
			if (count <= 0) continue;
			int itemType = FirstCandidateType(sp);
			if (itemType <= 0) continue;
			counts.TryGetValue(itemType, out int existing);
			counts[itemType] = existing + count;
		}

		// Pass 2 (see header).
		var sweepSeen = new HashSet<SimplePredicate>();
		foreach (var predicate in seenPredicates)
		{
			AppendAlternatives(predicate.Common,  primaryCellCount, sweepSeen, counts);
			AppendAlternatives(predicate.Limited, primaryCellCount, sweepSeen, counts);
		}

		// Stable ordering (visual): controller first, then by descending count.
		var result = new List<Drop>(counts.Count);
		if (controllerItemType > 0)
		{
			result.Add(new Drop(controllerItemType, counts[controllerItemType]));
			counts.Remove(controllerItemType);
		}
		var sorted = new List<KeyValuePair<int, int>>(counts);
		sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
		foreach (var kv in sorted)
			result.Add(new Drop(kv.Key, kv.Value));

		return result.Count > 0 ? result : null;
	}

	// The cell's "primary" - first chain SimplePredicate with a non-empty
	// candidate list. Walks `Chain` to preserve `.or(...)` order across bucket
	// moves (same convention as TraceabilityPredicate.PreviewItem).
	private static SimplePredicate? FindPrimary(TraceabilityPredicate predicate)
	{
		foreach (var sp in predicate.Chain)
		{
			var items = sp.Candidates?.Invoke();
			if (items is null) continue;
			foreach (var it in items)
			{
				if (it is not null && !it.IsAir) return sp;
			}
		}
		return null;
	}

	private static int FirstCandidateType(SimplePredicate sp)
	{
		var items = sp.Candidates?.Invoke();
		if (items is null) return 0;
		foreach (var it in items)
		{
			if (it is not null && !it.IsAir) return it.type;
		}
		return 0;
	}

	// Pass 2 helper - add 1 of each bucket predicate that wasn't a Pass-1 primary
	// and whose type isn't already in the bag. sweepSeen dedups SP instances.
	private static void AppendAlternatives(
		List<SimplePredicate> bucket,
		Dictionary<SimplePredicate, int> primaryCellCount,
		HashSet<SimplePredicate> sweepSeen,
		Dictionary<int, int> counts)
	{
		foreach (var sp in bucket)
		{
			if (primaryCellCount.ContainsKey(sp)) continue; // already counted by Pass 1
			if (!sweepSeen.Add(sp)) continue;
			int itemType = FirstCandidateType(sp);
			if (itemType <= 0) continue;
			if (counts.ContainsKey(itemType)) continue;
			counts[itemType] = 1;
		}
	}
}
