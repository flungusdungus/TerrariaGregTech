#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Pattern.Error;
using Terraria;
using Terraria.Localization;

namespace GregTechCEuTerraria.Api.Pattern;

// Port of com.gregtechceu.gtceu.api.pattern.predicates.SimplePredicate.
//
// A single predicate test against the current cell in a multiblock match -
// wraps a `Func<MultiblockState, bool>` plus count/layer-count constraints,
// a slot name (for cross-predicate counting), and a `candidates` supplier
// for JEI/error display.
//
// Documented adaptations:
//   - `Predicate<MultiblockState>` -> `Func<MultiblockState, bool>`.
//   - `Supplier<BlockInfo[]>` (candidate `BlockInfo` upstream) -> `Func<Item[]>`.
//     We don't have BlockInfo (it's a Forge construct carrying BlockState +
//     BlockEntity + ItemStack); the candidate set for our error UI is Items.
//   - `Component toolTips` -> `string toolTips` (no Component system).
//   - `nbtParser` field DROPPED - predicate-on-NBT isn't a pattern we use,
//     and the Forge `BlockEntity.saveWithFullMetadata()` round-trip has no
//     direct Terraria analogue.
//   - AIR test -> `!Main.tile[x,y].HasTile` at the anchor coordinate (recall
//     MultiblockState carries the 2x2 block's top-left tile; "air" for a
//     multiblock cell means the anchor tile is empty).
public class SimplePredicate
{
	// "Any block matches" - the {#} wildcard in upstream's aisle DSL.
	public static readonly SimplePredicate ANY = new("any", _ => true, null);

	// "Air at this cell" - the hollow-interior marker for upstream multis.
	public static readonly SimplePredicate AIR = new("air",
		state => !Main.tile[state.PosX, state.PosY].HasTile, null);

	public Func<Item[]>? Candidates;
	public Func<MultiblockState, bool>? Predicate;
	public List<string>? ToolTips;
	public int MinCount = -1;
	public int MaxCount = -1;
	public int MinLayerCount = -1;
	public int MaxLayerCount = -1;
	public int PreviewCount = -1;
	public bool DisableRenderFormed = false;
	public IO Io = IO.BOTH;
	public string? SlotName;

	public readonly string Type;

	public SimplePredicate() : this("unknown") { }

	public SimplePredicate(string type)
	{
		Type = type;
	}

	public SimplePredicate(Func<MultiblockState, bool> predicate, Func<Item[]>? candidates) : this("unknown")
	{
		Predicate = predicate;
		Candidates = candidates;
	}

	public SimplePredicate(string type, Func<MultiblockState, bool> predicate, Func<Item[]>? candidates) : this(type)
	{
		Predicate = predicate;
		Candidates = candidates;
	}

	public SimplePredicate BuildPredicate() => this;

	// JEI/error-UI tooltips. Upstream returns List<Component>; we return strings.
	public List<string> GetToolTips(TraceabilityPredicate? predicates)
	{
		var result = new List<string>();
		if (ToolTips != null) result.AddRange(ToolTips);
		if (MinCount == MaxCount && MaxCount != -1)
			result.Add($"Exactly {MinCount}");
		else if (MinCount != MaxCount && MinCount != -1 && MaxCount != -1)
			result.Add($"Between {MinCount} and {MaxCount}");
		else
		{
			if (MinCount != -1) result.Add($"At least {MinCount}");
			if (MaxCount != -1) result.Add($"At most {MaxCount}");
		}
		if (predicates is null) return result;
		if (predicates.IsSingle()) result.Add("(Only one allowed)");
		if (predicates.HasAir())   result.Add("(Can be air)");
		return result;
	}

	public bool Test(MultiblockState state)
	{
		if (Predicate is null) return false;
		if (Predicate(state))
			return CheckInnerConditions(state);
		return false;
	}

	public bool TestLimited(MultiblockState state)
	{
		if (TestGlobal(state) && TestLayer(state))
			return CheckInnerConditions(state);
		return false;
	}

	private bool CheckInnerConditions(MultiblockState state)
	{
		if (DisableRenderFormed)
		{
			var renderMask = state.MatchContext.GetOrCreate("renderMask", () => new HashSet<long>());
			renderMask.Add(MultiblockState.PackPos(state.PosX, state.PosY));
		}
		if (Io != IO.BOTH)
		{
			if (state.Io == IO.BOTH)
				state.Io = Io;
			else if (state.Io != Io)
				state.Io = IO.NONE;   // sentinel - upstream uses `null`, we use NONE
		}
		// NB: upstream also handles `nbtParser` here. Dropped - see header.
		if (SlotName != null)
		{
			var slots = state.MatchContext.GetOrCreate("slots", () => new Dictionary<long, HashSet<string>>());
			long packed = MultiblockState.PackPos(state.PosX, state.PosY);
			if (!slots.TryGetValue(packed, out var set))
			{
				set = new HashSet<string>();
				slots[packed] = set;
			}
			set.Add(SlotName);
		}
		return true;
	}

	public bool TestGlobal(MultiblockState state)
	{
		if (MinCount == -1 && MaxCount == -1) return true;
		bool baseResult = Predicate is not null && Predicate(state);
		state.GlobalCount.TryGetValue(this, out int count);
		count += baseResult ? 1 : 0;
		state.GlobalCount[this] = count;
		if (MaxCount == -1 || count <= MaxCount) return baseResult;
		state.SetError(new SinglePredicateError(this, 0));
		return false;
	}

	public bool TestLayer(MultiblockState state)
	{
		if (MinLayerCount == -1 && MaxLayerCount == -1) return true;
		bool baseResult = Predicate is not null && Predicate(state);
		state.LayerCount.TryGetValue(this, out int count);
		count += baseResult ? 1 : 0;
		state.LayerCount[this] = count;
		if (MaxLayerCount == -1 || count <= MaxLayerCount) return baseResult;
		state.SetError(new SinglePredicateError(this, 2));
		return false;
	}

	public List<Item> GetCandidates()
	{
		if (Candidates is null) return new List<Item>();
		var arr = Candidates();
		var list = new List<Item>(arr.Length);
		foreach (var it in arr)
			if (it != null && !it.IsAir)
				list.Add(it);
		return list;
	}
}
