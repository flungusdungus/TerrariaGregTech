#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Pattern.Error;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

namespace GregTechCEuTerraria.Api.Pattern;

// Matcher for variable-size multiblock shapes - upstream's `.setRepeatable
// (min, max)` aisle modifier on `BlockPattern`. Internally tries each
// `(verticalN, horizontalN)` combination, materialises the shape via
// `RepeatableShape.Build(...)`, and runs the standard `BlockPattern` matcher.
// First successful pair wins.
//
// === Iteration order =======================================================
//
// Walks vertical (outer) and horizontal (inner) repetitions from MIN upward -
// preferring the smallest valid structure. Smaller matches first means a
// 5x5 cleanroom in a 15x15 budget still resolves at the 5x5 size rather than
// over-matching. Step is honoured per-axis (HorizontalStep=2 walks {3,5,7,...}
// for odd-only widths).
public sealed class RepeatableBlockPattern : IBlockPattern
{
	public readonly RepeatableShape Shape;
	public readonly IReadOnlyDictionary<char, TraceabilityPredicate> Predicates;

	// Cache of materialised `BlockPattern`s keyed by `(verticalN, horizontalN)`.
	// Each is constructed once on first use - shape arrays are immutable so
	// the inner pattern is safe to reuse across match attempts.
	private readonly Dictionary<(int v, int h), BlockPattern> _byPair = new();

	public RepeatableBlockPattern(RepeatableShape shape,
		IReadOnlyDictionary<char, TraceabilityPredicate> predicates)
	{
		Shape = shape;
		Predicates = predicates;
	}

	public bool CheckPatternAt(MultiblockState state, bool savePredicate = false)
	{
		// Best-error tracking - across all size attempts, prefer the most
		// informative error to report. A SinglePredicateError of type 1
		// ("global minimum unmet") or 3 ("layer minimum unmet") tells the
		// player "your shape is correct, you're just missing this part" -
		// the most actionable feedback they can get. Any other error is a
		// cell-mismatch ("wrong block at X, Y"), which is uninformative for
		// the typical "I built it but forgot a hatch" case - the smaller-form
		// attempt at a larger structure naturally hits cell mismatches first,
		// but those aren't what the player needs to know.
		PatternError? bestError = null;
		bool          bestIsMissingPart = false;

		void Consider(PatternError? err)
		{
			if (err is null) return;
			bool isMissingPart = err is Error.SinglePredicateError spe && (spe.Type == 1 || spe.Type == 3);
			// Prefer the first missing-part error we see. Otherwise, prefer
			// the first error of any kind. Never overwrite a missing-part
			// error with a cell-mismatch one.
			if (bestError is null || (isMissingPart && !bestIsMissingPart))
			{
				bestError = err;
				bestIsMissingPart = isMissingPart;
			}
		}

		int hStep = System.Math.Max(1, Shape.HorizontalStep);
		int vStep = System.Math.Max(1, Shape.VerticalStep);

		// Axis-aware iteration. In Horizontal mode, Body columns repeat along
		// the X axis - `MinHorizontalRepeats`/`MaxHorizontalRepeats` define the
		// range and the vertical-axis loop collapses to one pass at v=0
		// (Shape.Build ignores verticalN when Axis == Horizontal). Defensive
		// fallback: if a Horizontal shape forgot to set horizontal repeats but
		// has vertical ones (legacy authoring), use those instead - the user's
		// pre-sketched assembly_line shape keeps `MinVerticalRepeats: 3,
		// MaxVerticalRepeats: 15` and that should still work just by adding
		// `Axis: RepeatAxis.Horizontal`.
		int minN, maxN, stepN;
		bool horizontalMode = Shape.Axis == TerrariaCompat.Machine.Multiblock.RepeatAxis.Horizontal;
		if (horizontalMode)
		{
			if (Shape.MaxHorizontalRepeats >= Shape.MinHorizontalRepeats && Shape.MaxHorizontalRepeats > 0)
			{
				minN  = Shape.MinHorizontalRepeats;
				maxN  = Shape.MaxHorizontalRepeats;
				stepN = hStep;
			}
			else
			{
				minN  = Shape.MinVerticalRepeats;
				maxN  = Shape.MaxVerticalRepeats;
				stepN = vStep;
			}
		}
		else
		{
			minN  = Shape.MinVerticalRepeats;
			maxN  = Shape.MaxVerticalRepeats;
			stepN = vStep;
		}

		for (int v = minN; v <= maxN; v += stepN)
		{
			if (horizontalMode)
			{
				// Single-axis: vertical pass is 1 attempt, parameter is `horizontalN`.
				if (!_byPair.TryGetValue((0, v), out var pattern))
				{
					pattern = new BlockPattern(Shape.Build(0, v), Predicates);
					_byPair[(0, v)] = pattern;
				}
				if (pattern.CheckPatternAt(state, savePredicate))
				{
					state.MatchContext.Set("verticalRepeats",   0);
					state.MatchContext.Set("horizontalRepeats", v);
					return true;
				}
				Consider(state.Error);
				continue;
			}

			// Vertical mode - preserve the original 2D iteration shape.
			for (int h = Shape.MinHorizontalRepeats; h <= Shape.MaxHorizontalRepeats; h += hStep)
			{
				if (!_byPair.TryGetValue((v, h), out var pattern))
				{
					pattern = new BlockPattern(Shape.Build(v, h), Predicates);
					_byPair[(v, h)] = pattern;
				}
				if (pattern.CheckPatternAt(state, savePredicate))
				{
					state.MatchContext.Set("verticalRepeats",   v);
					state.MatchContext.Set("horizontalRepeats", h);
					return true;
				}
				Consider(state.Error);
			}
			// If horizontal range is empty (single-axis shape), at least try
			// vertical alone - the inner loop above won't execute when
			// MaxHorizontalRepeats < MinHorizontalRepeats.
			if (Shape.MaxHorizontalRepeats < Shape.MinHorizontalRepeats)
			{
				if (!_byPair.TryGetValue((v, 0), out var pattern))
				{
					pattern = new BlockPattern(Shape.Build(v, 0), Predicates);
					_byPair[(v, 0)] = pattern;
				}
				if (pattern.CheckPatternAt(state, savePredicate))
				{
					state.MatchContext.Set("verticalRepeats",   v);
					state.MatchContext.Set("horizontalRepeats", 0);
					return true;
				}
				Consider(state.Error);
			}
		}
		state.SetError(bestError);
		return false;
	}

	// Preview the largest form - gives the player the full outline they have
	// room to build. Reuses cached pattern if any.
	public BlockPattern GetPreviewPattern()
	{
		int v, h;
		if (Shape.Axis == TerrariaCompat.Machine.Multiblock.RepeatAxis.Horizontal)
		{
			v = 0;
			// Use the horizontal range, with the same fallback to vertical
			// fields the matcher uses.
			h = Shape.MaxHorizontalRepeats >= Shape.MinHorizontalRepeats && Shape.MaxHorizontalRepeats > 0
				? Shape.MaxHorizontalRepeats
				: Shape.MaxVerticalRepeats;
		}
		else
		{
			v = Shape.MaxVerticalRepeats;
			h = System.Math.Max(0, Shape.MaxHorizontalRepeats);
		}
		if (!_byPair.TryGetValue((v, h), out var pattern))
		{
			pattern = new BlockPattern(Shape.Build(v, h), Predicates);
			_byPair[(v, h)] = pattern;
		}
		return pattern;
	}
}
