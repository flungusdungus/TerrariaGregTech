#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Util;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Detector;

// Port of common.cover.detector.FluidDetectorCover. Signal proportional to
// host fluid-tank fill, collapsed to binary by DetectorCover.
public class FluidDetectorCover : DetectorCover
{
	public FluidDetectorCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide) { }

	public override bool CanAttach() => base.CanAttach() && GetFluidHandler() != null;

	protected override void Update()
	{
		if (CoverHolder.GetOffsetTimer() % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(20) != 0) return;

		var handler = GetFluidHandler();
		if (handler == null) return;

		int storedFluid = 0;
		int fluidCapacity = 0;
		for (int tank = 0; tank < handler.TankCount; tank++)
		{
			var content = handler.GetTank(tank);
			if (!content.IsEmpty) storedFluid += content.Amount;
			fluidCapacity += handler.GetCapacity(tank);
		}

		if (fluidCapacity == 0) return;

		SetRedstoneSignalOutput(RedstoneUtil.ComputeRedstoneValue(storedFluid, fluidCapacity, IsInverted));
	}

	protected IFluidHandler? GetFluidHandler() => CoverHolder as IFluidHandler;
}
