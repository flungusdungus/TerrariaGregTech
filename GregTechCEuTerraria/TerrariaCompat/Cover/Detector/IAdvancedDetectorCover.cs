#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Cover.Detector;

// UI-read surface shared by the advanced detector covers (item / fluid /
// energy) so the cover settings popup can read invert + min/max thresholds
// without per-type casts. Item/fluid extend Item/FluidDetectorCover and energy
// extends EnergyDetectorCover - separate trees, hence the shared interface.
// The per-type extras (latch for item/fluid, EU/percent for energy) and all
// mutation stay type-specific, through CoverBehavior.ApplySetting.
public interface IAdvancedDetectorCover
{
	bool IsInverted { get; }
	long MinValue { get; }
	long MaxValue { get; }
}
