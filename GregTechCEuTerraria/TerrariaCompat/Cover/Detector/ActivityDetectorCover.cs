#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Detector;

// Port of common.cover.detector.ActivityDetectorCover. Full signal while the
// host is actively working.
public class ActivityDetectorCover : DetectorCover
{
	public ActivityDetectorCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide) { }

	public override bool CanAttach() => base.CanAttach() && GetWorkable() != null;

	protected override void Update()
	{
		if (CoverHolder.GetOffsetTimer() % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(20) != 0) return;

		var workable = GetWorkable();
		if (workable == null) return;

		bool isCurrentlyWorking = workable.IsActive() && workable.IsWorkingEnabled();
		SetRedstoneSignalOutput(isCurrentlyWorking != IsInverted ? 15 : 0);
	}

	protected IWorkable? GetWorkable() => CoverHolder as IWorkable;
}
