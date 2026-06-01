#nullable enable
using GregTechCEuTerraria.Api.Capability;
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Cable;

// LOCKED - port of EnergyRoutePath. Adaptations: path is (x,y) coords
// (our CableLayer is a flat dict, not BlockEntity refs); Target cached
// (invalidated on net rebuild); single route-path type, no IRoutePath parent.
public sealed class EnergyRoutePath
{
	public (int x, int y) TargetCablePos { get; }

	// IODirection.None = endpoint at the SAME CELL as the target cable (wire
	// behind machine). Endpoint receives as "internal" delivery, IOConfig
	// side filters don't apply.
	public IODirection TargetFacing { get; }

	public IEnergyContainer Target { get; }

	public IReadOnlyList<(int x, int y)> Cables { get; }

	public int Distance { get; }

	// Sum of per-cable LossPerAmp along the path; an amp packet arrives at
	// `voltage - loss`. Long to match upstream (int overflows on long lossy nets).
	public long Loss { get; }

	public EnergyRoutePath(
		(int x, int y) targetCablePos,
		IODirection targetFacing,
		IEnergyContainer target,
		IReadOnlyList<(int x, int y)> cables,
		int distance,
		long loss)
	{
		TargetCablePos = targetCablePos;
		TargetFacing = targetFacing;
		Target = target;
		Cables = cables;
		Distance = distance;
		Loss = loss;
	}
}
