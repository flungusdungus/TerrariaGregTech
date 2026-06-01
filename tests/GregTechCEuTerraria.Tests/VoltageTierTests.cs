using System.Linq;
using GregTechCEuTerraria.Common.Energy;
using Xunit;

namespace GregTechCEuTerraria.Tests;

public class VoltageTierTests
{
	[Fact]
	public void HasExactlyFifteenTiers()
	{
		Assert.Equal(15, VoltageTiers.Count);
		Assert.Equal(15, VoltageTiers.All.Count());
	}

	[Fact]
	public void EachTierIsQuadrupleThePrevious()
	{
		for (int i = 1; i < VoltageTiers.Count; i++)
		{
			long prev = VoltageTiers.Voltage((VoltageTier)(i - 1));
			long cur  = VoltageTiers.Voltage((VoltageTier)i);
			Assert.Equal(prev * 4L, cur);
		}
	}

	[Fact]
	public void EndpointsMatchUpstream()
	{
		Assert.Equal(8L,          VoltageTiers.Voltage(VoltageTier.ULV));
		Assert.Equal(32L,         VoltageTiers.Voltage(VoltageTier.LV));
		Assert.Equal(2147483648L, VoltageTiers.Voltage(VoltageTier.MAX));
	}

	[Fact]
	public void ShortNamesAreUniqueAndMatchUpstream()
	{
		var names = VoltageTiers.All.Select(t => t.ShortName).ToList();
		Assert.Equal(names.Count, names.Distinct().Count());
		Assert.Equal("ULV", VoltageTiers.ShortName(VoltageTier.ULV));
		Assert.Equal("LuV", VoltageTiers.ShortName(VoltageTier.LuV));
		Assert.Equal("ZPM", VoltageTiers.ShortName(VoltageTier.ZPM));
		Assert.Equal("OpV", VoltageTiers.ShortName(VoltageTier.OpV));
	}

	[Fact]
	public void LongNamesAreNonEmpty()
	{
		Assert.Equal("Insane Voltage",     VoltageTiers.LongName(VoltageTier.IV));
		Assert.Equal("Ultimate Voltage",   VoltageTiers.LongName(VoltageTier.UV));
		Assert.All(VoltageTiers.All, info => Assert.False(string.IsNullOrEmpty(info.LongName)));
	}

	[Fact]
	public void MinTierForVoltageRoundsUp()
	{
		Assert.Equal(VoltageTier.ULV, VoltageTiers.MinTierForVoltage(0));
		Assert.Equal(VoltageTier.ULV, VoltageTiers.MinTierForVoltage(-100));
		Assert.Equal(VoltageTier.ULV, VoltageTiers.MinTierForVoltage(8));
		Assert.Equal(VoltageTier.LV,  VoltageTiers.MinTierForVoltage(9));
		Assert.Equal(VoltageTier.LV,  VoltageTiers.MinTierForVoltage(32));
		Assert.Equal(VoltageTier.MV,  VoltageTiers.MinTierForVoltage(33));
		Assert.Equal(VoltageTier.HV,  VoltageTiers.MinTierForVoltage(512));
		Assert.Equal(VoltageTier.MAX, VoltageTiers.MinTierForVoltage(long.MaxValue));
	}

	[Fact]
	public void MaxTierForVoltageRoundsDown()
	{
		Assert.Equal(VoltageTier.ULV, VoltageTiers.MaxTierForVoltage(8));
		Assert.Equal(VoltageTier.ULV, VoltageTiers.MaxTierForVoltage(31));
		Assert.Equal(VoltageTier.LV,  VoltageTiers.MaxTierForVoltage(32));
		Assert.Equal(VoltageTier.LV,  VoltageTiers.MaxTierForVoltage(127));
		Assert.Equal(VoltageTier.MV,  VoltageTiers.MaxTierForVoltage(128));
		Assert.Equal(VoltageTier.MAX, VoltageTiers.MaxTierForVoltage(long.MaxValue));
	}

	// GTUtil.getOCTierByVoltage port - the tier math the overclock chain uses
	// for recipeTier / maximumTier.
	[Fact]
	public void OcTierByVoltageMatchesUpstream()
	{
		Assert.Equal((int)VoltageTier.ULV, VoltageTiers.OcTierByVoltage(2));
		Assert.Equal((int)VoltageTier.ULV, VoltageTiers.OcTierByVoltage(8));
		Assert.Equal((int)VoltageTier.LV,  VoltageTiers.OcTierByVoltage(9));
		Assert.Equal((int)VoltageTier.LV,  VoltageTiers.OcTierByVoltage(31));
		Assert.Equal((int)VoltageTier.LV,  VoltageTiers.OcTierByVoltage(32));
		Assert.Equal((int)VoltageTier.MV,  VoltageTiers.OcTierByVoltage(128));
		Assert.Equal((int)VoltageTier.HV,  VoltageTiers.OcTierByVoltage(512));
		Assert.Equal((int)VoltageTier.EV,  VoltageTiers.OcTierByVoltage(2048));
	}

	// GTUtil.getTierByVoltage port - like OcTierByVoltage but caps at MAX for
	// voltages above the int range.
	[Fact]
	public void TierByVoltageCapsAtMaxAboveIntRange()
	{
		Assert.Equal((int)VoltageTier.EV,  VoltageTiers.TierByVoltage(2048));
		Assert.Equal((int)VoltageTier.MAX, VoltageTiers.TierByVoltage(2147483648L));
		Assert.Equal((int)VoltageTier.MAX, VoltageTiers.TierByVoltage(long.MaxValue));
	}

	// Upstream overclockLogicEVRecipeHVMachineTest - an EV recipe on an HV
	// machine is rejected: its recipe tier exceeds the machine's OC tier cap.
	// (This is the gate ELECTRIC_OVERCLOCK applies before overclocking.)
	[Fact]
	public void EvRecipeTierExceedsHvMachineTier()
	{
		int evRecipeTier  = VoltageTiers.TierByVoltage(VoltageTiers.Voltage(VoltageTier.EV));
		int hvMachineTier = (int)VoltageTier.HV;
		Assert.True(evRecipeTier > hvMachineTier);
	}
}
