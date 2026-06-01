#nullable enable
using GregTechCEuTerraria.Api.Pipenet;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.LongDistance;

// Unified item + fluid LD pipe layer. Two adjacent cells form a link iff both
// exist AND share the same Type - so an item LD line and a fluid LD line can
// cross without merging (the renderer + the net both honor this). LD pipes bend
// and branch freely (unlike laser/optical), so connectivity is the plain
// same-type adjacency test; the per-arm renderer handles corners.
public sealed class LongDistancePipeLayer : GridLayer<LongDistancePipeCell>
{
	public override bool Connects(int x1, int y1, int x2, int y2)
	{
		var a = CellAt(x1, y1);
		var b = CellAt(x2, y2);
		if (a is null || b is null) return false;
		return a.Value.Type == b.Value.Type;
	}
}
