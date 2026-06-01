using GregTechCEuTerraria.TerrariaCompat.Pipelike.Cable;
using Xunit;

namespace GregTechCEuTerraria.Tests;

public class PerTickLongCounterTests
{
	[Fact]
	public void Increment_AccumulatesWithinSameTick()
	{
		var c = new PerTickLongCounter();
		c.Increment(100, 5);
		c.Increment(100, 7);
		Assert.Equal(12, c.Get(100));
	}

	[Fact]
	public void GetLast_ReturnsPreviousTickValueWhenAdvancingOneTick()
	{
		var c = new PerTickLongCounter();
		c.Increment(100, 9);
		Assert.Equal(9, c.Get(100));

		// Advance one tick - previous current -> last; current resets to default.
		Assert.Equal(9, c.GetLast(101));
		Assert.Equal(0, c.Get(101));
	}

	[Fact]
	public void GetLast_ResetsToDefaultWhenSkippingMoreThanOneTick()
	{
		var c = new PerTickLongCounter(defaultValue: -1);
		c.Set(100, 50);
		Assert.Equal(50, c.Get(100));

		// Skip two ticks - last and current both go to default.
		Assert.Equal(-1, c.GetLast(102));
		Assert.Equal(-1, c.Get(102));
	}

	[Fact]
	public void Set_OverwritesCurrentTickValue()
	{
		var c = new PerTickLongCounter();
		c.Increment(50, 10);
		c.Set(50, 3);
		Assert.Equal(3, c.Get(50));
	}
}

public class AveragingPerTickCounterTests
{
	[Fact]
	public void Average_AcrossFullBuffer()
	{
		var c = new AveragingPerTickCounter(defaultValue: 0, length: 4);
		c.Set(1, 4);
		c.Set(2, 4);
		c.Set(3, 4);
		c.Set(4, 4);
		Assert.Equal(4.0, c.GetAverage(4));
	}

	[Fact]
	public void Average_PartialFill_CountsZerosForUnfilledSlots()
	{
		var c = new AveragingPerTickCounter(defaultValue: 0, length: 4);
		c.Set(1, 8);
		c.Set(2, 8);
		// Buffer is [8, 8, 0, 0] after two filled ticks -> avg 4.
		Assert.Equal(4.0, c.GetAverage(2));
	}

	[Fact]
	public void Average_LongSkipResetsBuffer()
	{
		var c = new AveragingPerTickCounter(defaultValue: 0, length: 4);
		c.Set(1, 10);
		c.Set(2, 10);
		c.Set(3, 10);
		c.Set(4, 10);
		// Skip beyond buffer length -> full reset.
		Assert.Equal(0.0, c.GetAverage(100));
	}

	[Fact]
	public void GetLast_TracksCurrentTickValueOnly()
	{
		var c = new AveragingPerTickCounter(defaultValue: 0, length: 4);
		c.Set(1, 11);
		Assert.Equal(11, c.GetLast(1));
		c.Set(2, 22);
		Assert.Equal(22, c.GetLast(2));
	}

	[Fact]
	public void Increment_AccumulatesWithinTick()
	{
		var c = new AveragingPerTickCounter(defaultValue: 0, length: 4);
		c.Increment(1, 3);
		c.Increment(1, 4);
		Assert.Equal(7, c.GetLast(1));
	}
}
