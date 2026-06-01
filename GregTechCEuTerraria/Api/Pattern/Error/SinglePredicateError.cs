#nullable enable
using System.Collections.Generic;

using Terraria;
using Terraria.Localization;

namespace GregTechCEuTerraria.Api.Pattern.Error;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.pattern.error.SinglePredicateError.
//
// Emitted when a SimplePredicate's count/layer-count constraint fails (too
// few / too many of this kind of block in the structure). `type` selects
// which of the four constraint forms the player is told about:
//   0 -> too many globally   (predicate.maxCount)
//   1 -> too few globally    (predicate.minCount)
//   2 -> too many per layer  (predicate.maxLayerCount)
//   3 -> too few per layer   (predicate.minLayerCount)
public class SinglePredicateError : PatternError
{
	public SimplePredicate Predicate { get; }
	public int Type { get; }

	public SinglePredicateError(SimplePredicate predicate, int type)
	{
		Predicate = predicate;
		Type = type;
	}

	public override List<List<Item>> GetCandidates() => new() { Predicate.GetCandidates() };

	public override string ErrorInfo
	{
		get
		{
			// Type -> what failed + required count + actual count (best-effort:
			// the global count is populated in MultiblockState; the per-layer
			// count is wiped between layers so we can't always recover it).
			int required = -1;
			int actual   = -1;
			string what  = "";
			switch (Type)
			{
				case 0:
					required = Predicate.MaxCount;
					if (WorldState is not null) WorldState.GlobalCount.TryGetValue(Predicate, out actual);
					what = "too many";
					break;
				case 1:
					required = Predicate.MinCount;
					if (WorldState is not null) WorldState.GlobalCount.TryGetValue(Predicate, out actual);
					what = "not enough";
					break;
				case 2:
					required = Predicate.MaxLayerCount;
					what = "too many per layer";
					break;
				case 3:
					required = Predicate.MinLayerCount;
					what = "not enough per layer";
					break;
			}

			// Name(s) of the block(s) this predicate would have accepted.
			// max=1 keeps the line short - ability predicates can have 15+
			// tier variants (ULV Output Hatch / LV Output Hatch / ...) which
			// blow the tooltip width. `JoinCandidateNames` appends "+N more"
			// suffix when truncated.
			string names = JoinCandidateNames(Predicate.GetCandidates(), max: 1);

			string actualPart = actual >= 0 ? $" (have {actual})" : "";
			string namePart   = names.Length > 0 ? $" of {names}" : "";
			return $"{what}{namePart}: need {required}{actualPart}";
		}
	}

	// Same content as ErrorInfo - the count message is already short, no need
	// to split. One yielded line.
	public override System.Collections.Generic.IEnumerable<string> ErrorDetailLines()
	{
		yield return ErrorInfo;
	}

	private static string JoinCandidateNames(List<Item> candidates, int max)
	{
		if (candidates.Count == 0) return "";
		var sb = new System.Text.StringBuilder();
		int n = System.Math.Min(candidates.Count, max);
		for (int i = 0; i < n; i++)
		{
			if (i > 0) sb.Append(i == n - 1 && candidates.Count <= max ? " or " : ", ");
			sb.Append(Lang.GetItemName(candidates[i].type).Value);
		}
		if (candidates.Count > max) sb.Append($" +{candidates.Count - max} more");
		return sb.ToString();
	}
}
