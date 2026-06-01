#nullable enable
namespace GregTechCEuTerraria.Common.Machine.Trait.Hpca;

// Port of com.gregtechceu.gtceu.common.machine.trait.hpca.
// HPCAComputationProviderTrait.
//
// A grid component that supplies CWU/t and demands cooling. Both getters
// return 0 while damaged (verbatim).
public class HPCAComputationProviderTrait : HPCAComponentTrait
{
	private readonly int _cwuPerTick;
	private readonly int _coolingPerTick;

	public HPCAComputationProviderTrait(int upkeepEUt, int maxEUt, bool canBeDamaged,
		bool allowBridging, int cwuPerTick, int coolingPerTick)
		: base(upkeepEUt, maxEUt, canBeDamaged, allowBridging)
	{
		_cwuPerTick     = cwuPerTick;
		_coolingPerTick = coolingPerTick;
	}

	public int GetCoolingPerTick() => IsDamaged ? 0 : _coolingPerTick;
	public int GetCWUPerTick()     => IsDamaged ? 0 : _cwuPerTick;
}
