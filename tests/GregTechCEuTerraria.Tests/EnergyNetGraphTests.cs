using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Cable;
using Xunit;

namespace GregTechCEuTerraria.Tests;

public class EnergyNetGraphTests
{
	// Helper - wires of a single material/size with arbitrary loss for one test.
	private static CableCell Cell(VoltageTier voltage, int amp = 1, int loss = 0, byte size = 1) =>
		new("test_material", size, false, voltage, amp, loss);

	[Fact]
	public void EmptyLayerYieldsNoComponents()
	{
		Assert.Empty(EnergyNetGraph.Build(new CableLayer()));
	}

	[Fact]
	public void SingleCableIsItsOwnComponent()
	{
		var l = new CableLayer();
		l.Set(5, 5, Cell(VoltageTier.LV, amp: 2));
		var comps = EnergyNetGraph.Build(l);
		Assert.Single(comps);
		Assert.Single(comps[0].Cells);
		Assert.Equal(VoltageTier.LV, comps[0].EffectiveTier);
		Assert.Equal(2, comps[0].EffectiveAmperage);
	}

	[Fact]
	public void OrthogonallyAdjacentCablesJoin()
	{
		var l = new CableLayer();
		l.Set(0, 0, Cell(VoltageTier.MV));
		l.Set(1, 0, Cell(VoltageTier.MV));
		l.Set(2, 0, Cell(VoltageTier.MV));
		l.Set(2, 1, Cell(VoltageTier.MV));
		l.Set(2, 2, Cell(VoltageTier.MV));
		var comps = EnergyNetGraph.Build(l);
		Assert.Single(comps);
		Assert.Equal(5, comps[0].Cells.Count);
	}

	[Fact]
	public void DiagonallyAdjacentCablesStaySeparate()
	{
		var l = new CableLayer();
		l.Set(0, 0, Cell(VoltageTier.LV));
		l.Set(1, 1, Cell(VoltageTier.LV));
		var comps = EnergyNetGraph.Build(l);
		Assert.Equal(2, comps.Count);
	}

	[Fact]
	public void DisjointNetworksAreCountedSeparately()
	{
		var l = new CableLayer();
		// Network A
		l.Set(0, 0, Cell(VoltageTier.LV));
		l.Set(1, 0, Cell(VoltageTier.LV));
		// Network B - far away
		l.Set(50, 50, Cell(VoltageTier.HV));
		l.Set(51, 50, Cell(VoltageTier.HV));
		l.Set(52, 50, Cell(VoltageTier.HV));

		var comps = EnergyNetGraph.Build(l);
		Assert.Equal(2, comps.Count);
		Assert.Contains(comps, c => c.Cells.Count == 2 && c.EffectiveTier == VoltageTier.LV);
		Assert.Contains(comps, c => c.Cells.Count == 3 && c.EffectiveTier == VoltageTier.HV);
	}

	[Fact]
	public void MixedTierCablesSplitIntoPerTierComponents()
	{
		// Deliberate divergence from upstream: cables only link to a
		// same-voltage-tier neighbour (CableLayer.Connects), so a row of
		// mixed tiers does NOT merge into one weakest-link network - each
		// run of equal-tier cables forms its own isolated component.
		var l = new CableLayer();
		l.Set(0, 0, Cell(VoltageTier.HV));
		l.Set(1, 0, Cell(VoltageTier.HV));
		l.Set(2, 0, Cell(VoltageTier.LV));
		l.Set(3, 0, Cell(VoltageTier.EV));
		var comps = EnergyNetGraph.Build(l);
		Assert.Equal(3, comps.Count);
		Assert.Contains(comps, c => c.Cells.Count == 2 && c.EffectiveTier == VoltageTier.HV);
		Assert.Contains(comps, c => c.Cells.Count == 1 && c.EffectiveTier == VoltageTier.LV);
		Assert.Contains(comps, c => c.Cells.Count == 1 && c.EffectiveTier == VoltageTier.EV);
	}

	[Fact]
	public void EffectiveAmperageIsMinOfMembers()
	{
		var l = new CableLayer();
		l.Set(0, 0, Cell(VoltageTier.LV, amp: 4));
		l.Set(1, 0, Cell(VoltageTier.LV, amp: 1));   // bottleneck
		l.Set(2, 0, Cell(VoltageTier.LV, amp: 4));
		var comps = EnergyNetGraph.Build(l);
		Assert.Equal(1, comps[0].EffectiveAmperage);
	}

	[Fact]
	public void MaxLossPerAmpIsMaxOfMembers()
	{
		// Worst conductor in chain caps the whole network.
		var l = new CableLayer();
		l.Set(0, 0, Cell(VoltageTier.LV, loss: 1));
		l.Set(1, 0, Cell(VoltageTier.LV, loss: 8));   // worst link
		l.Set(2, 0, Cell(VoltageTier.LV, loss: 2));
		var comps = EnergyNetGraph.Build(l);
		Assert.Equal(8, comps[0].MaxLossPerAmp);
	}

	[Fact]
	public void WireSizeScalesEffectiveAmperage()
	{
		// hex iron (size 16, base 2 amp) = 32A; double iron (size 2, base 2) = 4A.
		var l = new CableLayer();
		l.Set(0, 0, Cell(VoltageTier.MV, amp: 2, size: 16));
		l.Set(1, 0, Cell(VoltageTier.MV, amp: 2, size: 2));   // bottleneck
		var comps = EnergyNetGraph.Build(l);
		Assert.Equal(4, comps[0].EffectiveAmperage);
	}

	[Fact]
	public void RingNetworkVisitedOnce()
	{
		// 3x3 hollow ring - proves visited set works (no infinite re-enqueue).
		var l = new CableLayer();
		for (int x = 0; x <= 2; x++) { l.Set(x, 0, Cell(VoltageTier.LV)); l.Set(x, 2, Cell(VoltageTier.LV)); }
		l.Set(0, 1, Cell(VoltageTier.LV));
		l.Set(2, 1, Cell(VoltageTier.LV));
		var comps = EnergyNetGraph.Build(l);
		Assert.Single(comps);
		Assert.Equal(8, comps[0].Cells.Count);
	}

	[Fact]
	public void ComponentCellsCarryTheirIndividualMaterial()
	{
		// Two same-tier cables of different materials/amperage/loss join into
		// one component; each cell keeps its own denormalized CableCell value.
		var l = new CableLayer();
		var ironHV   = new CableCell("iron",   1, false, VoltageTier.HV, 2, 3);
		var copperHV = new CableCell("copper", 1, false, VoltageTier.HV, 1, 2);
		l.Set(0, 0, ironHV);
		l.Set(1, 0, copperHV);
		var comps = EnergyNetGraph.Build(l);
		Assert.Single(comps);
		Assert.Equal(ironHV,   comps[0].Cells[(0, 0)]);
		Assert.Equal(copperHV, comps[0].Cells[(1, 0)]);
	}
}

// NOTE: EnergyNetworkTickTests was removed - its subject `EnergyNetwork`
// (TerrariaCompat/Energy/Network/EnergyNetwork.cs) genuinely depends on tML
// (Terraria.Audio, Microsoft.Xna.Framework, WorldGen, TieredEnergyMachine,
// CableLayerSystem) and cannot be compiled into the pure-logic test project.
