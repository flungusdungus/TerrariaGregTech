using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Cable;
using Xunit;

namespace GregTechCEuTerraria.Tests;

public class CableLayerTests
{
	// All tests use synthetic CableCell values - the cable layer is pure logic,
	// so it doesn't care whether the material id refers to a real registered
	// material. Voltage/amperage/loss values can be arbitrary too.
	private static CableCell Cell(string material = "iron", byte size = 1, VoltageTier voltage = VoltageTier.LV, int amp = 1, int loss = 0, bool insulated = false) =>
		new(material, size, insulated, voltage, amp, loss);

	[Fact]
	public void EmptyLayerHasNothing()
	{
		var l = new CableLayer();
		Assert.Equal(0, l.Count);
		Assert.False(l.Has(0, 0));
		Assert.Null(l.CellAt(0, 0));
	}

	[Fact]
	public void SetRoundtripsCell()
	{
		var l = new CableLayer();
		var c = Cell(voltage: VoltageTier.HV, amp: 3, loss: 2);
		l.Set(10, 20, c);
		Assert.True(l.Has(10, 20));
		Assert.Equal(c, l.CellAt(10, 20));
	}

	[Fact]
	public void UlvCellIsDistinctFromAbsent()
	{
		// ULV (tier 0) must roundtrip as a real present cable, not register as
		// missing - the v1 schema's +1-offset encoding bug is no longer relevant
		// (we store struct cells directly) but the contract still applies.
		var l = new CableLayer();
		l.Set(5, 5, Cell(voltage: VoltageTier.ULV));
		Assert.True(l.Has(5, 5));
		Assert.Equal(VoltageTier.ULV, l.CellAt(5, 5)!.Value.Voltage);
	}

	[Fact]
	public void SetOverwritesPreviousCell()
	{
		var l = new CableLayer();
		l.Set(0, 0, Cell(voltage: VoltageTier.LV));
		l.Set(0, 0, Cell(voltage: VoltageTier.EV, amp: 4));
		Assert.Equal(VoltageTier.EV, l.CellAt(0, 0)!.Value.Voltage);
		Assert.Equal(4, l.CellAt(0, 0)!.Value.BaseAmperage);
		Assert.Equal(1, l.Count);
	}

	[Fact]
	public void IdempotentSetSkipsDirtyFlag()
	{
		var l = new CableLayer();
		var c = Cell();
		l.Set(0, 0, c);
		l.ClearDirty();
		l.Set(0, 0, c); // same cell value - should not re-dirty
		Assert.False(l.IsDirty);
	}

	[Fact]
	public void RemoveErasesEntry()
	{
		var l = new CableLayer();
		l.Set(1, 1, Cell(voltage: VoltageTier.MV));
		Assert.True(l.Remove(1, 1));
		Assert.False(l.Has(1, 1));
		Assert.False(l.Remove(1, 1));
	}

	[Fact]
	public void ConnectionMaskRespectsAllFourSides()
	{
		var l = new CableLayer();
		l.Set(5, 5, Cell());   // center
		l.Set(5, 4, Cell());   // N
		l.Set(6, 5, Cell());   // E
		Assert.Equal(0b1001, l.ConnectionMask(5, 5));  // N + E
		l.Set(5, 6, Cell());   // S
		l.Set(4, 5, Cell());   // W
		Assert.Equal(0b1111, l.ConnectionMask(5, 5));  // all four
	}

	[Fact]
	public void ConnectionMaskIgnoresDiagonalNeighbors()
	{
		var l = new CableLayer();
		l.Set(0, 0, Cell());
		l.Set(1, 1, Cell());   // diagonal - must not count
		Assert.Equal(0, l.ConnectionMask(0, 0));
	}

	[Fact]
	public void ConnectionMaskConnectsSameTierAcrossMaterials()
	{
		// Material id does NOT gate a connection - only voltage tier does
		// (see CableLayer.Connects). Two same-tier cables of different
		// materials still connect.
		var l = new CableLayer();
		l.Set(0, 0, Cell("iron", voltage: VoltageTier.LV));
		l.Set(0, 1, Cell("copper", voltage: VoltageTier.LV));
		Assert.Equal(0b0010, l.ConnectionMask(0, 0));  // S only
	}

	[Fact]
	public void ConnectionMaskIgnoresMismatchedTierNeighbors()
	{
		// Deliberate divergence from upstream: a cable only links to a
		// same-voltage-tier neighbour, so a tier mismatch reads as no
		// connection (each tier forms its own isolated network).
		var l = new CableLayer();
		l.Set(0, 0, Cell("iron", voltage: VoltageTier.LV));
		l.Set(0, 1, Cell("iron", voltage: VoltageTier.HV));
		Assert.Equal(0, l.ConnectionMask(0, 0));
	}

	[Fact]
	public void TotalAmperageScalesByWireSize()
	{
		var iron2A = Cell(amp: 2, size: 1); Assert.Equal(2,  iron2A.TotalAmperage);
		var iron2Ax4 = Cell(amp: 2, size: 4); Assert.Equal(8,  iron2Ax4.TotalAmperage);
		var iron2Ax16 = Cell(amp: 2, size: 16); Assert.Equal(32, iron2Ax16.TotalAmperage);
	}
}
