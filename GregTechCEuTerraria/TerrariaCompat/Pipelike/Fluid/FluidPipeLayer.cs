#nullable enable
using GregTechCEuTerraria.Api.Pipenet;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Fluid;

public sealed class FluidPipeLayer : GridLayer<FluidPipeCell>
{
	// Same-material = same net (we drop upstream's paint mark; size doesn't
	// gate). Containment-proof / temperature / throughput are per-cell gates
	// on the routing tick, not topology.
	public override bool Connects(int x1, int y1, int x2, int y2)
	{
		var a = CellAt(x1, y1);
		var b = CellAt(x2, y2);
		return a is not null && b is not null && a.Value.MaterialId == b.Value.MaterialId;
	}
}
