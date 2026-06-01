#nullable enable
using GregTechCEuTerraria.Api.Pipenet;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.ItemPipe;

public sealed class ItemPipeLayer : GridLayer<ItemPipeCell>
{
	// Same-material = same net. Size + Restrictive don't gate connectivity.
	public override bool Connects(int x1, int y1, int x2, int y2)
	{
		var a = CellAt(x1, y1);
		var b = CellAt(x2, y2);
		return a is not null && b is not null && a.Value.MaterialId == b.Value.MaterialId;
	}
}
