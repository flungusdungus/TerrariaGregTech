using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;
using Xunit;

namespace GregTechCEuTerraria.Tests;

// Structural validity of every hand-authored shape in `MultiblockShapes.cs`.
// Does NOT validate predicates / controller markers (those bind later when
// MachineDefinition rows wire each shape to a char->predicate map). Only checks
// that the shape itself is well-formed - non-empty, rectangular, and (for
// RepeatableShape) materializes correctly at the relevant axis's Min/Max.
//
// Shapes that are still authoring stubs (every row commented out -> empty
// array) are skipped, not failed - they're TODO markers, not bugs.
public class MultiblockShapesTests
{
	private static IEnumerable<(string Name, string[] Rows)> EnumerateFixedShapes()
	{
		foreach (var f in typeof(MultiblockShapes).GetFields(BindingFlags.Public | BindingFlags.Static))
		{
			if (f.FieldType == typeof(string[]) && f.GetValue(null) is string[] rows)
				yield return (f.Name, rows);
		}
	}

	private static IEnumerable<(string Name, RepeatableShape Shape)> EnumerateRepeatableShapes()
	{
		foreach (var f in typeof(MultiblockShapes).GetFields(BindingFlags.Public | BindingFlags.Static))
		{
			if (f.FieldType == typeof(RepeatableShape) && f.GetValue(null) is RepeatableShape s)
				yield return (f.Name, s);
		}
	}

	// Sanity: reflection finds the things we expect to be there.
	[Fact]
	public void DiscoversBothShapeKinds()
	{
		Assert.NotEmpty(EnumerateFixedShapes());
		Assert.NotEmpty(EnumerateRepeatableShapes());
	}

	[Fact]
	public void FixedShapesAreRectangular()
	{
		var failures = new List<string>();
		int skipped = 0;
		foreach (var (name, rows) in EnumerateFixedShapes())
		{
			if (rows.Length == 0) { skipped++; continue; }
			int w = rows[0].Length;
			if (w == 0) { failures.Add($"{name}: row 0 is empty"); continue; }
			for (int r = 1; r < rows.Length; r++)
			{
				if (rows[r].Length != w)
					failures.Add($"{name}: row {r} width {rows[r].Length} != row 0 width {w}");
			}
		}
		Assert.True(failures.Count == 0,
			$"{failures.Count} non-rectangular shape(s) ({skipped} stub(s) skipped):\n  " +
			string.Join("\n  ", failures));
	}

	[Fact]
	public void RepeatableShapesHaveValidBounds()
	{
		var failures = new List<string>();
		foreach (var (name, s) in EnumerateRepeatableShapes())
		{
			if (s.MinVerticalRepeats < 0)
				failures.Add($"{name}: MinVerticalRepeats {s.MinVerticalRepeats} < 0");
			if (s.MaxVerticalRepeats < s.MinVerticalRepeats)
				failures.Add($"{name}: MaxVerticalRepeats {s.MaxVerticalRepeats} < MinVerticalRepeats {s.MinVerticalRepeats}");
			if (s.MinHorizontalRepeats < 0)
				failures.Add($"{name}: MinHorizontalRepeats {s.MinHorizontalRepeats} < 0");
			if (s.MaxHorizontalRepeats < s.MinHorizontalRepeats)
				failures.Add($"{name}: MaxHorizontalRepeats {s.MaxHorizontalRepeats} < MinHorizontalRepeats {s.MinHorizontalRepeats}");
		}
		Assert.True(failures.Count == 0, string.Join("\n", failures));
	}

	[Fact]
	public void RepeatableShapesBuildRectangularly()
	{
		var failures = new List<string>();
		int skipped = 0;
		foreach (var (name, s) in EnumerateRepeatableShapes())
		{
			// Authoring stub - body/head/tail all empty: nothing to materialise.
			if (s.Head.Length == 0 && s.Body.Length == 0 && s.Tail.Length == 0)
			{
				skipped++;
				continue;
			}

			// Horizontal: Head/Body/Tail must share row count.
			if (s.Axis == RepeatAxis.Horizontal &&
				(s.Body.Length != s.Head.Length || s.Tail.Length != s.Head.Length))
			{
				failures.Add(
					$"{name} (horizontal): Head/Body/Tail row counts differ " +
					$"(Head={s.Head.Length}, Body={s.Body.Length}, Tail={s.Tail.Length})");
				continue;
			}

			// Walk the (vertical, horizontal) pairs the shape actually exercises.
			// Two-axis shapes (cleanroom-style) need both N values matched per
			// attempt - feeding (vMin, 0) to a shape whose rows depend on
			// horizontalN > 0 produces a non-rectangular result that doesn't
			// reflect a real match attempt.
			var attempts = new List<(int v, int h)>();
			if (s.Axis == RepeatAxis.Horizontal)
			{
				int v = s.MaxVerticalRepeats;  // horizontal mode ignores verticalN
				attempts.Add((v, s.MinHorizontalRepeats));
				attempts.Add((v, s.MaxHorizontalRepeats));
			}
			else
			{
				// Vertical-axis shape. If it also scales horizontally, walk the
				// (min,min) and (max,max) corners.
				bool twoAxis = s.MaxHorizontalRepeats >= s.MinHorizontalRepeats && s.MaxHorizontalRepeats > 0;
				int hMin = twoAxis ? s.MinHorizontalRepeats : 0;
				int hMax = twoAxis ? s.MaxHorizontalRepeats : 0;
				attempts.Add((s.MinVerticalRepeats, hMin));
				attempts.Add((s.MaxVerticalRepeats, hMax));
			}
			foreach (var (v, h) in attempts)
			{
				string[] built;
				try { built = s.Build(v, h); }
				catch (Exception ex)
				{
					failures.Add($"{name}.Build({v},{h}) threw: {ex.Message}");
					continue;
				}
				if (built.Length == 0)
				{
					if (s.Head.Length + s.Tail.Length + (v * s.Body.Length) != 0)
						failures.Add($"{name}.Build({v},{h}) produced 0 rows but segments are non-empty");
					continue;
				}
				int w = built[0].Length;
				for (int r = 1; r < built.Length; r++)
				{
					if (built[r].Length != w)
					{
						failures.Add(
							$"{name}.Build({v},{h}): row {r} width {built[r].Length} != row 0 width {w}");
						break;
					}
				}
			}
		}
		Assert.True(failures.Count == 0,
			$"{failures.Count} repeatable-shape failure(s) ({skipped} stub(s) skipped):\n  " +
			string.Join("\n  ", failures));
	}

	// ============================================================================
	// `RepeatableShape.Build` itself - exercised on synthetic shapes, independent
	// of whatever the user has typed in MultiblockShapes.cs.
	// ============================================================================

	[Fact]
	public void Build_Vertical_StacksHeadBodyNTimesThenTail()
	{
		var s = new RepeatableShape(
			Head: new[] { (RowPattern)"AAA", "BBB" },
			Body: new[] { (RowPattern)"CCC" },
			Tail: new[] { (RowPattern)"DDD" },
			MinVerticalRepeats: 1,
			MaxVerticalRepeats: 3,
			Axis: RepeatAxis.Vertical);
		Assert.Equal(new[] { "AAA", "BBB", "CCC", "DDD" }, s.Build(1, 0));
		Assert.Equal(new[] { "AAA", "BBB", "CCC", "CCC", "CCC", "DDD" }, s.Build(3, 0));
	}

	[Fact]
	public void Build_Vertical_NZeroOmitsBodyEntirely()
	{
		var s = new RepeatableShape(
			Head: new[] { (RowPattern)"AA" },
			Body: new[] { (RowPattern)"BB" },
			Tail: new[] { (RowPattern)"CC" },
			MinVerticalRepeats: 0,
			MaxVerticalRepeats: 5);
		Assert.Equal(new[] { "AA", "CC" }, s.Build(0, 0));
	}

	[Fact]
	public void Build_Horizontal_ConcatsLeftBodyNRight()
	{
		var s = new RepeatableShape(
			Head: new[] { (RowPattern)"L", "L" },
			Body: new[] { (RowPattern)"M", "M" },
			Tail: new[] { (RowPattern)"R", "R" },
			MinVerticalRepeats: 0,
			MaxVerticalRepeats: 0,
			MinHorizontalRepeats: 1,
			MaxHorizontalRepeats: 4,
			Axis: RepeatAxis.Horizontal);
		Assert.Equal(new[] { "LMR", "LMR" },     s.Build(0, 1));
		Assert.Equal(new[] { "LMMMR", "LMMMR" }, s.Build(0, 3));
	}

	// BuildHorizontal tolerates mismatched Head/Body/Tail row counts - missing
	// rows are treated as empty (an authoring slip surfaces as a non-rectangular
	// shape rather than a crash). Mirror that contract explicitly.
	[Fact]
	public void Build_Horizontal_MismatchedRowCountsProducesDegraded()
	{
		var s = new RepeatableShape(
			Head: new[] { (RowPattern)"L", "L" },
			Body: new[] { (RowPattern)"M" },
			Tail: new[] { (RowPattern)"R", "R" },
			MinVerticalRepeats: 0,
			MaxVerticalRepeats: 0,
			MinHorizontalRepeats: 1,
			MaxHorizontalRepeats: 1,
			Axis: RepeatAxis.Horizontal);
		// Row 0: Head[0]="L" + Body[0]="M" + Tail[0]="R" -> "LMR"
		// Row 1: Head[1]="L" + (Body[1] missing) + Tail[1]="R" -> "LR"
		Assert.Equal(new[] { "LMR", "LR" }, s.Build(0, 1));
	}
}
