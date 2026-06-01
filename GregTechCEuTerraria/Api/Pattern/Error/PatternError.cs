#nullable enable
using System.Collections.Generic;

using Terraria;
using Terraria.Localization;

namespace GregTechCEuTerraria.Api.Pattern.Error;

// Port of com.gregtechceu.gtceu.api.pattern.error.PatternError.
//
// Base class for pattern-match failures. Carries a back-pointer to the
// MultiblockState that produced the failure (so subclasses can read position
// / candidates), plus an `ErrorInfo` describing the failure for the player.
//
// Documented adaptations:
//   - `Component getErrorInfo()` -> `string ErrorInfo` (no Component system).
//   - `ItemStack` candidates -> Terraria `Item` (subclasses iterate via the
//     SimplePredicate.candidates supplier when ported; the base falls back
//     to a placeholder message until we have an item-candidate convention).
public class PatternError
{
	public MultiblockState? WorldState { get; private set; }

	public void SetWorldState(MultiblockState worldState) => WorldState = worldState;

	public int GetX() => WorldState?.PosX ?? 0;
	public int GetY() => WorldState?.PosY ?? 0;

	// Candidate items per predicate variant - the things this cell would have
	// accepted. Upstream returns List<List<ItemStack>>; we return List<List<Item>>.
	public virtual List<List<Item>> GetCandidates()
	{
		var candidates = new List<List<Item>>();
		var predicate = WorldState?.Predicate;
		if (predicate is null) return candidates;
		foreach (var common in predicate.Common)
			candidates.Add(common.GetCandidates());
		foreach (var limited in predicate.Limited)
			candidates.Add(limited.GetCandidates());
		return candidates;
	}

	// One-line summary suitable for the in-GUI status footer. Caps the
	// candidate list at 2 items so the line fits on screen - full list is
	// available via `ErrorDetailLines()` for the world hover tooltip.
	public virtual string ErrorInfo
	{
		get
		{
			var candidates = GetCandidates();
			var sb = new System.Text.StringBuilder();
			int shown = 0;
			int total = 0;
			foreach (var candidate in candidates)
			{
				if (candidate.Count == 0) continue;
				total++;
				if (shown < 2)
				{
					if (shown > 0) sb.Append(", ");
					sb.Append(Lang.GetItemName(candidate[0].type).Value);
					shown++;
				}
			}
			if (total > shown) sb.Append($" +{total - shown} more");
			return $"Wrong block at ({GetX()}, {GetY()}): expected {sb} (found: {FoundBlockDescriptor()})";
		}
	}

	// What is ACTUALLY placed at the failing cell anchor - the missing half of a
	// useful error. "expected X (found: Y)" tells the player whether they placed
	// the WRONG block (found = some other block) or a RIGHT block that's
	// MISALIGNED (found = empty, because multi cells are 2x2 and the matcher
	// anchors on the 2x2 grid - a casing one tile off-grid leaves the anchor
	// empty). Resolution mirrors MultiblockPreviewHover.TileTypeName (tile ->
	// sibling item name).
	protected string FoundBlockDescriptor()
	{
		int x = GetX(), y = GetY();
		if (x < 0 || x >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY) return "out of world";
		var t = Main.tile[x, y];
		if (!t.HasTile) return "empty - is the block aligned to the 2x2 grid?";
		var modTile = Terraria.ModLoader.TileLoader.GetTile(t.TileType);
		if (modTile != null)
		{
			if (modTile.Mod.TryFind<Terraria.ModLoader.ModItem>(modTile.Name, out var mi))
				return Lang.GetItemName(mi.Type).Value;
			return modTile.Name;
		}
		string vanilla = Lang.GetMapObjectName(Terraria.Map.MapHelper.TileToLookup(t.TileType, 0));
		return string.IsNullOrEmpty(vanilla) ? $"Tile #{t.TileType}" : vanilla;
	}

	// Multi-line form for the world hover tooltip. Each yielded line should
	// fit on one tooltip row - callers do `lines.Add(line)` per item. Default
	// emits a header + one short bullet per candidate group (typically casing
	// / each accepted hatch family / etc.).
	public virtual IEnumerable<string> ErrorDetailLines()
	{
		yield return $"Wrong block at ({GetX()}, {GetY()}) - found: {FoundBlockDescriptor()}";
		yield return "Expected one of:";
		var candidates = GetCandidates();
		int shown = 0;
		foreach (var group in candidates)
		{
			if (group.Count == 0) continue;
			string name = Lang.GetItemName(group[0].type).Value;
			yield return group.Count > 1
				? $"  - {name} (+{group.Count - 1} more)"
				: $"  - {name}";
			if (++shown >= 6) { yield return "  - ..."; break; }
		}
	}
}
