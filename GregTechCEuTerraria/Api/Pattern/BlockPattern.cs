#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Machine.Feature.Multiblock;
using GregTechCEuTerraria.Api.Pattern.Error;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using PredicatesNs = GregTechCEuTerraria.Api.Pattern.Predicates;

namespace GregTechCEuTerraria.Api.Pattern;

// ADAPTED - 2D matcher equivalent of
// com.gregtechceu.gtceu.api.pattern.BlockPattern (657 LOC upstream, ~120 here).
//
// Upstream's matcher is heavily 3D-coupled - `.aisle("XXX", ...)` rows stack
// along an axis, the front-facing direction picks one of four 90deg rotations
// to try, and `setRepeatable(...)` aisles allow variable-height structures.
// Our 2D world strips most of that:
//
//   - The shape is a flat `string[]` (rows top-to-bottom, cols left-to-right).
//     No aisles.
//   - No rotation pass - Terraria placement has no facing.
//   - No mirror/flip pass for v1 (could add later; trivially: re-test with
//     each row's chars reversed).
//   - No in-matcher repeat - variable-size multis (DistillationTower etc.)
//     compose their final shape (top + Nxrepeat + bottom) at the caller
//     level and pass the materialised `string[]` here. The matcher tries
//     each candidate N in turn until one matches.
//   - 2x2-tile stepping: every shape cell == 2 Terraria tiles in each axis.
//     The shape's controller cell maps to the controller's anchor tile;
//     other cells map to anchor +/- `col*2, row*2`.
//
// === Algorithm ==============================================================
//
//   1. Find the shape cell whose char's predicate has IsController = true.
//      That's (controllerCol, controllerRow). The matcher needs exactly one.
//   2. Origin tile = controllerAnchor - (controllerCol * 2, controllerRow * 2).
//   3. Iterate every (col, row) cell:
//        - Compute tile coord (origin + col*2, origin + row*2).
//        - Call `state.Update(tileX, tileY, predicate)`.
//        - If `predicate.AddCache()`, record the tile in `state.Cache`.
//        - If the cell's MetaMachine implements `IMultiPart`, add to the
//          match-context "parts" set (subject to sharing rules).
//        - Run `predicate.Test(state)`. Fail -> return false.
//   4. Verify all per-predicate min-count constraints are met (each
//      `state.GlobalCount[predicate] >= predicate.MinCount`).
//   5. On success, the state's `Cache` + "parts" set describe the formed
//      structure; the controller persists them.
//
// `savePredicate` mirrors upstream - when true, the matcher records a
// `tileLong -> predicate` map in the match-context's "predicates" entry so
// post-match code can look up which predicate matched each cell (used by
// hatches in upstream to know their I/O role from the controller's pattern).
public class BlockPattern : IBlockPattern
{
	public readonly string[] Shape;
	public readonly IReadOnlyDictionary<char, TraceabilityPredicate> Predicates;

	// Controller-cell coords in shape space, computed once at ctor. Exposed
	// for the preview renderer (it needs the same anchor math the matcher
	// uses to position ghost cells relative to the controller's tile).
	public int ControllerCol { get; }
	public int ControllerRow { get; }

	// Width of each row (every row must be the same length).
	public readonly int Width;
	public readonly int Height;

	public BlockPattern(string[] shape, IReadOnlyDictionary<char, TraceabilityPredicate> predicates)
	{
		Shape = shape;
		// Universal '#' fallback - upstream convention is "interior empty /
		// don't-care" cell; in 2D those become walk-through gaps the matcher
		// shouldn't check at all. Saves every pattern factory from repeating
		// `['#'] = Predicates.Any()`. Caller-supplied mapping wins if present.
		if (!predicates.ContainsKey('#'))
		{
			var merged = new Dictionary<char, TraceabilityPredicate>(predicates) { ['#'] = PredicatesNs.Any() };
			Predicates = merged;
		}
		else
		{
			Predicates = predicates;
		}
		Height = shape.Length;
		Width = Height > 0 ? shape[0].Length : 0;
		ValidateShape();
		var (cc, cr) = FindController();
		ControllerCol = cc;
		ControllerRow = cr;
	}

	public BlockPattern GetPreviewPattern() => this;

	private void ValidateShape()
	{
		for (int r = 0; r < Shape.Length; r++)
		{
			if (Shape[r].Length != Width)
				throw new System.ArgumentException(
					$"BlockPattern: row {r} has length {Shape[r].Length}, expected {Width} " +
					"(all rows must be the same width).");
		}
	}

	private (int Col, int Row) FindController()
	{
		(int Col, int Row)? hit = null;
		for (int r = 0; r < Height; r++)
		{
			for (int c = 0; c < Width; c++)
			{
				char ch = Shape[r][c];
				if (Predicates.TryGetValue(ch, out var p) && p.IsController)
				{
					if (hit is not null)
						throw new System.ArgumentException(
							$"BlockPattern: more than one controller cell ('{Shape[hit.Value.Row][hit.Value.Col]}' " +
							$"at {hit.Value} and '{ch}' at ({c}, {r})). Shapes must have exactly one.");
					hit = (c, r);
				}
			}
		}
		if (hit is null)
			throw new System.ArgumentException(
				"BlockPattern: no controller cell found. Exactly one shape char must map to a " +
				"predicate built via `Predicates.Controller(...)` (IsController=true).");
		return hit.Value;
	}

	// Run the matcher. `state.ControllerPosX/Y` is the anchor tile of the
	// controller's 2x2 block. Returns true on match.
	public bool CheckPatternAt(MultiblockState state, bool savePredicate = false)
	{
		state.Clean();

		int originX = state.ControllerPosX - ControllerCol * 2;
		int originY = state.ControllerPosY - ControllerRow * 2;

		for (int row = 0; row < Height; row++)
		{
			// Each shape row = one layer (a horizontal Y-aligned strip in 2D,
			// upstream's per-aisle layer in 3D). `setMaxLayerLimited(N)` /
			// `setMinLayerLimited(N)` are PER-layer, so the layer counter
			// resets when we move to a new row. Without this reset,
			// `setMaxLayerLimited(1)` silently acts as a global cap and
			// shapes with one allowed-per-row hatch on multiple rows fail.
			state.LayerCount.Clear();
			for (int col = 0; col < Width; col++)
			{
				char ch = Shape[row][col];
				if (!Predicates.TryGetValue(ch, out var predicate))
				{
					state.SetError(new PatternStringError($"gtceu.multiblock.pattern.error.unmapped_char:{ch}"));
					return false;
				}
				int tileX = originX + col * 2;
				int tileY = originY + row * 2;
				state.SetError(null);
				if (!state.Update(tileX, tileY, predicate))
					return false;

				if (predicate.AddCache())
				{
					state.AddPosCache(tileX, tileY);
					if (savePredicate)
					{
						var preds = state.MatchContext.GetOrCreate("predicates",
							() => new Dictionary<long, TraceabilityPredicate>());
						preds[MultiblockState.PackPos(tileX, tileY)] = predicate;
					}
				}

				// If the cell hosts a part machine, add it to the parts set
				// (unless it's already bound to a different controller and
				// disallows sharing).
				bool canPartShared = true;
				if (state.GetMachine() is IMultiPart part)
				{
					if (!predicate.IsAny())
					{
						bool partOwned = part.IsFormed()
							&& !part.HasController(state.ControllerPosX, state.ControllerPosY);
						if (partOwned && !part.CanShared())
						{
							canPartShared = false;
							state.SetError(new PatternStringError("multiblocked.pattern.error.share"));
						}
						else
						{
							var parts = state.MatchContext.GetOrCreate("parts", () => new HashSet<IMultiPart>());
							parts.Add(part);
						}
					}
				}

				if (!predicate.Test(state) || !canPartShared)
				{
					// Cell didn't match any of its allowed predicates (player put
					// the wrong block here) AND no inner predicate set a more
					// specific error (e.g. MaxCount overflow -> SinglePredicateError).
					// Without this catch-all, the matcher returns false with
					// state.Error=null and the player sees a bare "Structure not
					// formed" line with no hint of WHICH cell is wrong. Default
					// PatternError.ErrorInfo reads "Wrong block at (X, Y):
					// expected <candidates>" off the current cell's predicate
					// candidates - actionable for every pattern type.
					if (state.Error == null)
						state.SetError(new Error.PatternError());
					return false;
				}

				// Record IO role for this cell (consumed by the controller
				// when aggregating its parts' handlers).
				var ioMap = state.MatchContext.GetOrCreate("ioMap", () => new Dictionary<long, Capability.Recipe.IO>());
				ioMap[MultiblockState.PackPos(tileX, tileY)] = state.Io;
			}
		}

		// All cells matched - verify min-count constraints. A predicate that
		// has MinCount > 0 but never matched any cell is also a failure: seed
		// every reachable Limited predicate at count 0 here so the iteration
		// below catches them. (Without this, e.g. a cleanroom missing its
		// required maintenance hatch silently forms because the maintenance
		// predicate never landed in GlobalCount.)
		foreach (var predicate in Predicates.Values)
		{
			foreach (var sp in predicate.Limited)
			{
				if (sp.MinCount > 0 && !state.GlobalCount.ContainsKey(sp))
					state.GlobalCount[sp] = 0;
			}
		}
		foreach (var kv in state.GlobalCount)
		{
			var sp = kv.Key;
			if (sp.MinCount != -1 && kv.Value < sp.MinCount)
			{
				state.SetError(new SinglePredicateError(sp, 1));
				return false;
			}
		}

		state.SetError(null);
		return true;
	}
}
