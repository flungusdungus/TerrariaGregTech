#nullable enable
using System.Collections.Generic;

namespace GregTechCEuTerraria.Api.Capability;

// Port of com.gregtechceu.gtceu.api.capability.IOpticalComputationProvider.
//
// MUST be implemented on any multiblock that uses Transmitter Computation
// Hatches in its structure. Computation is measured in CWU/t (Compute Work
// Units per tick) - recipes that consume CWU/t request them from the
// provider every tick.
//
// The `seen` parameter prevents infinite recursion when computation networks
// loop back on themselves (e.g. a Network Switch bridging two HPCAs).
//
// Documented adaptations: verbatim - all three methods preserved with
// default overloads that seed a fresh `seen` set.
public interface IOpticalComputationProvider
{
	// Request up to `cwut` CWU/t. Returns what could actually be supplied.
	// Implementors should expect this every tick computation is needed.
	int RequestCWUt(int cwut, bool simulate) =>
		RequestCWUt(cwut, simulate, NewSeen(this));

	int RequestCWUt(int cwut, bool simulate, ICollection<IOpticalComputationProvider> seen);

	// Max CWU/t this provider can supply (steady-state).
	int GetMaxCWUt() => GetMaxCWUt(NewSeen(this));

	int GetMaxCWUt(ICollection<IOpticalComputationProvider> seen);

	// Whether this provider can "bridge" with other providers - Network
	// Switch checks this to decide if it can chain multiple HPCAs.
	bool CanBridge() => CanBridge(NewSeen(this));

	bool CanBridge(ICollection<IOpticalComputationProvider> seen);

	private static ICollection<IOpticalComputationProvider> NewSeen(IOpticalComputationProvider self)
	{
		var s = new HashSet<IOpticalComputationProvider>();
		s.Add(self);
		return s;
	}
}
