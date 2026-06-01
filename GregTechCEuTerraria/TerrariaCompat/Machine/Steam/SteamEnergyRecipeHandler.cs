#nullable enable
using System;
using GregTechCEuTerraria.Api.Machine.Trait;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Steam;

// Adapted port of SteamEnergyRecipeHandler. SimpleSteamMachine runs electric
// recipes (EU cost) and pays them as steam: conversionRate mB/EU (1x LP, 2x HP).
// Standalone helper (not IRecipeHandler<EnergyStack>) bridging the collapsed
// TryDrainEU hook. Drain is atomic (simulate-then-commit) since TryDrainEU is
// all-or-nothing per tick with no simulate pass.
public sealed class SteamEnergyRecipeHandler
{
	private readonly NotifiableFluidTank _steamTank;

	// Upstream getConversionRate(): 1.0 LP / 2.0 HP.
	public double ConversionRate { get; }

	public SteamEnergyRecipeHandler(NotifiableFluidTank steamTank, double conversionRate)
	{
		_steamTank     = steamTank;
		ConversionRate = conversionRate;
	}

	// Atomic drain. totalSteam = ceil(EU x rate) - upstream-verbatim.
	public bool TryDrainEnergy(long totalEU, bool simulate)
	{
		if (totalEU <= 0) return true;
		long steam = (long)Math.Ceiling(totalEU * ConversionRate);
		if (steam <= 0) return true;
		if (steam > int.MaxValue) return false;
		int need = (int)steam;
		if (_steamTank.DrainInternal(need, simulate: true).Amount < need) return false;
		if (!simulate) _steamTank.DrainInternal(need, simulate: false);
		return true;
	}

	// EU = floor(steam / rate) - inverse of TryDrainEnergy's ceil, so never
	// reports more EU than the drain can actually pay.
	public long StoredEu
	{
		get
		{
			var stack = _steamTank.GetFluidInTank(0);
			return stack.IsEmpty ? 0L : (long)(stack.Amount / ConversionRate);
		}
	}

	// Raw steam in mB - upstream getStored() / getCapacity().
	public long Stored
	{
		get
		{
			var stack = _steamTank.GetFluidInTank(0);
			return stack.IsEmpty ? 0L : stack.Amount;
		}
	}

	public long Capacity => _steamTank.GetTankCapacity(0);
}
