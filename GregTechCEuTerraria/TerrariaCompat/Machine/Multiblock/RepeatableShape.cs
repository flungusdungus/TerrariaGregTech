#nullable enable
using System.Collections.Generic;
using System.Text;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

// Variable-size multiblock shape. Authored in MultiblockShapes.cs; consumed
// by RepeatableBlockPattern which tries each (verticalN, horizontalN) combo.
//
// Vertical: Head rows + Body x MinVerticalRepeats..MaxVerticalRepeats + Tail.
// Horizontal: each RowPattern(Head, Body, Tail) has Body x MinHorizontalRepeats..MaxHorizontalRepeats.
// HorizontalStep/VerticalStep advances N (HorizontalStep=2 -> odd-only widths for cleanroom).
// RowPattern implicitly converts from string.
//
// RepeatAxis.Vertical (default): Body rows repeat downward.
// RepeatAxis.Horizontal: outer Head/Body/Tail are column-segments (same row
// count each); row r = Head[r] + Body[r] x N + Tail[r]. Inner RowPattern.Body
// unused - write each as a single fixed string.
public enum RepeatAxis { Vertical, Horizontal }

public sealed record RepeatableShape(
	RowPattern[] Head,
	RowPattern[] Body,
	RowPattern[] Tail,
	int MinVerticalRepeats,
	int MaxVerticalRepeats,
	int MinHorizontalRepeats = 0,
	int MaxHorizontalRepeats = 0,
	int VerticalStep   = 1,
	int HorizontalStep = 1,
	RepeatAxis Axis = RepeatAxis.Vertical)
{
	public string[] Build(int verticalN, int horizontalN)
	{
		if (verticalN   < 0) verticalN   = 0;
		if (horizontalN < 0) horizontalN = 0;
		if (Axis == RepeatAxis.Horizontal)
			return BuildHorizontal(horizontalN);
		var rows = new List<string>(Head.Length + verticalN * Body.Length + Tail.Length);
		foreach (var rp in Head) rows.Add(rp.Build(horizontalN));
		for (int v = 0; v < verticalN; v++)
			foreach (var rp in Body) rows.Add(rp.Build(horizontalN));
		foreach (var rp in Tail) rows.Add(rp.Build(horizontalN));
		return rows.ToArray();
	}

	// Horizontal: Head/Body/Tail are column-segments; rowCount tolerates
	// mismatched lengths (missing row -> empty) so authoring slips degrade
	// rather than crash. Build(0) collapses inner RowPattern to Head+Center+Tail.
	private string[] BuildHorizontal(int horizontalN)
	{
		int rowCount = System.Math.Max(System.Math.Max(Head.Length, Body.Length), Tail.Length);
		var rows = new string[rowCount];
		var sb = new StringBuilder();
		for (int r = 0; r < rowCount; r++)
		{
			sb.Clear();
			if (r < Head.Length) sb.Append(Head[r].Build(0));
			if (r < Body.Length)
			{
				string bodyChunk = Body[r].Build(0);
				for (int x = 0; x < horizontalN; x++) sb.Append(bodyChunk);
			}
			if (r < Tail.Length) sb.Append(Tail[r].Build(0));
			rows[r] = sb.ToString();
		}
		return rows;
	}
}

// One row of a RepeatableShape. Materialises as Head + Body x N + Tail, or
// (when Center is non-empty) Head + Body x N/2 + Center + Body x N/2 + Tail.
// Implicit `string -> RowPattern` maps "XXXXX" -> new("XXXXX", "", "") (row
// goes into Head so it materialises at h=0 - required since single-axis
// shapes default Min/Max horizontal repeats to 0).
public sealed record RowPattern(string Head, string Body, string Tail, string Center = "")
{
	public string Build(int horizontalN)
	{
		if (horizontalN <= 0 && Body.Length == 0)
			return Head + Center + Tail;

		int bodyCharCount = Body.Length * horizontalN;
		int total = Head.Length + bodyCharCount + Center.Length + Tail.Length;
		var sb = new StringBuilder(total);
		sb.Append(Head);
		if (Center.Length == 0)
		{
			for (int i = 0; i < horizontalN; i++) sb.Append(Body);
		}
		else
		{
			// Cleanroom keeps N odd via HorizontalStep=2 so Center lands middle.
			int half = horizontalN / 2;
			for (int i = 0; i < half; i++) sb.Append(Body);
			sb.Append(Center);
			for (int i = 0; i < half; i++) sb.Append(Body);
		}
		sb.Append(Tail);
		return sb.ToString();
	}

	public static implicit operator RowPattern(string fixedRow) => new(fixedRow, "", "");
}
