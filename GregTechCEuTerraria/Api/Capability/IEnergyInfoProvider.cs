#nullable enable
using System.Numerics;

namespace GregTechCEuTerraria.Api.Capability;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.capability.IEnergyInfoProvider.
// DO NOT modify behavior; mirror upstream changes only.
//
// Display + aggregate-stats surface for energy-bearing entities. Splits off
// from IEnergyContainer because cables, transformers, multiblock controllers
// want to advertise stored/capacity totals (sum of all hatches, sum of all
// batteries, etc.) without implementing the full accept/output contract.
//
// The BigInteger surface matters at UV+ tiers where summing energy across a
// big network overflows long: V[MAX] = 2_147_483_648 EU/t x OutputAmperage x
// many machines can exceed 9.2 x 10^18 (long max) within a normal play
// session. Upstream uses java.math.BigInteger; we use System.Numerics.BigInteger
// (no third-party dep).
//
// Adaptations:
//   - `isOneProbeHidden` - TheOneProbe is a Forge mod for tooltips; default
//     stays as `false` and no Terraria consumer reads it. Kept for upstream
//     shape parity (subclasses that override it still cost nothing).
public interface IEnergyInfoProvider
{
	// Snapshot of (capacity, stored) for display. Record type matches
	// upstream's nested `record EnergyInfo(BigInteger, BigInteger)`.
	public readonly record struct EnergyInfo(BigInteger Capacity, BigInteger Stored);

	EnergyInfo GetEnergyInfo();

	long GetInputPerSec();

	long GetOutputPerSec();

	// True if this provider's internal totals exceed `long` range. Display
	// widgets use this to pick BigInteger formatting over long formatting.
	// Abstract (no default) at this level - upstream only defaults it on
	// IEnergyContainer (to false), so aggregators like EnergyContainerList
	// can return true without inheriting a misleading override.
	bool SupportsBigIntEnergyValues();

	// Hide capacity/stored from external tooltip overlays. Useful for
	// cables (per-tick stored is misleading) and creative energy sources.
	bool IsOneProbeHidden() => false;
}
