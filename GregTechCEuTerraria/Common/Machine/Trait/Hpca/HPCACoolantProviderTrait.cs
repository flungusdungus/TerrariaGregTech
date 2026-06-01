#nullable enable
namespace GregTechCEuTerraria.Common.Machine.Trait.Hpca;

// Port of com.gregtechceu.gtceu.common.machine.trait.hpca.
// HPCACoolantProviderTrait.
//
// A grid component that provides cooling. Passive heat sinks have
// isActiveCooler=false + maxCoolantPerTick=0; active coolers drain coolant.
public class HPCACoolantProviderTrait : HPCAComponentTrait
{
	public int  CoolingAmount     { get; }
	public int  MaxCoolantPerTick { get; }
	public bool IsActiveCooler    { get; }

	public HPCACoolantProviderTrait(int upkeepEUt, int maxEUt, bool canBeDamaged,
		bool allowBridging, int coolingAmount, int maxCoolantPerTick, bool isActiveCooler)
		: base(upkeepEUt, maxEUt, canBeDamaged, allowBridging)
	{
		CoolingAmount     = coolingAmount;
		MaxCoolantPerTick = maxCoolantPerTick;
		IsActiveCooler    = isActiveCooler;
	}
}
