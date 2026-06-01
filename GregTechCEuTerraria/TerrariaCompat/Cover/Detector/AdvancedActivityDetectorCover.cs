#nullable enable
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Util;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Detector;

// Port of common.cover.detector.AdvancedActivityDetectorCover. Signal
// proportional to recipe progress instead of flat on-while-working.
public class AdvancedActivityDetectorCover : ActivityDetectorCover
{
	public AdvancedActivityDetectorCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide) { }

	protected override void Update()
	{
		if (CoverHolder.GetOffsetTimer() % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(20) != 0) return;

		var workable = GetWorkable();
		if (workable == null || workable.GetMaxProgress() == 0)
		{
			SetRedstoneSignalOutput(0);
			return;
		}

		int outputAmount = RedstoneUtil.ComputeRedstoneValue(
			workable.GetProgress(), workable.GetMaxProgress(), IsInverted);

		// Verbatim upstream off-state handling.
		if (!workable.IsWorkingEnabled() || !workable.IsActive())
			outputAmount = 0;

		SetRedstoneSignalOutput(outputAmount);
	}
}
