#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Util;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Detector;

// Port of common.cover.detector.EnergyDetectorCover. Signal proportional to
// stored EU / capacity, collapsed to binary by DetectorCover.
public class EnergyDetectorCover : DetectorCover
{
	public EnergyDetectorCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide) { }

	public override bool CanAttach() => base.CanAttach() && GetEnergyInfoProvider() != null;

	protected override void Update()
	{
		if (CoverHolder.GetOffsetTimer() % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(20) != 0) return;

		var provider = GetEnergyInfoProvider();
		if (provider == null) return;

		var energyInfo = provider.GetEnergyInfo();
		if (provider.SupportsBigIntEnergyValues())
		{
			if (energyInfo.Capacity.IsZero) return;
			SetRedstoneSignalOutput(
				RedstoneUtil.ComputeRedstoneValue(energyInfo.Stored, energyInfo.Capacity, IsInverted));
		}
		else
		{
			long storedEnergy = (long)energyInfo.Stored;
			long energyCapacity = (long)energyInfo.Capacity;
			if (energyCapacity == 0) return;
			SetRedstoneSignalOutput(
				RedstoneUtil.ComputeRedstoneValue(storedEnergy, energyCapacity, IsInverted));
		}
	}

	protected IEnergyInfoProvider? GetEnergyInfoProvider() => CoverHolder as IEnergyInfoProvider;
}
