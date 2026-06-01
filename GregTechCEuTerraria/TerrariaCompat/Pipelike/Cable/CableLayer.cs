#nullable enable
using GregTechCEuTerraria.Api.Pipenet;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Cable;

// Sparse cable map. CableCell is denormalised so this + EnergyNetGraph +
// EnergyNet stay MaterialRegistry-free (unit-testable without tML).
public sealed class CableLayer : GridLayer<CableCell>
{
	// DEVIATION: cables connect only to same-tier neighbours
	// (upstream links any). Mixed-tier joins are useless without a transformer.
	public override bool Connects(int x1, int y1, int x2, int y2)
	{
		var a = CellAt(x1, y1);
		var b = CellAt(x2, y2);
		return a is not null && b is not null && a.Value.Voltage == b.Value.Voltage;
	}
}
