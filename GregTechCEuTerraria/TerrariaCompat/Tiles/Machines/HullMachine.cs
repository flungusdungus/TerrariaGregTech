#nullable enable
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

// Verbatim port of upstream HullMachine (common/machine/electric/HullMachine.java).
// Tiered passthrough - capacity V[tier]*16, input/output 1A at tier voltage. The
// AE2 grid-node + multiblock PASSTHROUGH_HATCH ability bits are dropped (neither
// system is ported). Exists primarily as a crafting intermediate - every real
// machine assembles from a hull of its tier.
public sealed class HullMachine : TieredEnergyMachine
{
	public HullMachine() { }
	public HullMachine(VoltageTier tier) : base(tier) { }

	protected override string Label => Definition?.Label ?? "Machine Hull";

	public override bool CanAccept  => true;
	public override bool CanExtract => true;

	public override long EnergyCapacity => VoltageTiers.Voltage(Tier) * 16L;
}
