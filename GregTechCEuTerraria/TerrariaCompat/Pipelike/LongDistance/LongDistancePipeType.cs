#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.LongDistance;

// Port of com.gregtechceu.gtceu.api.pipenet.longdistance.LongDistancePipeType.
//
// Upstream models item/fluid as two singleton subclasses (LDItemPipeType /
// LDFluidPipeType) keyed into a static registry. We collapse that to an enum:
// the two LD pipe types never merge, and the only per-type difference is which
// capability the endpoint exposes + the min-length config value. Node.Mark
// (Item=1, Fluid=2) keeps the connected components separate, mirroring upstream's
// "can't merge unequal pipe types" guard.
public enum LongDistancePipeType : byte
{
	Item  = 0,
	Fluid = 1,
}

public static class LongDistancePipeTypeExtensions
{
	// Node mark - non-zero so item/fluid never satisfy AreMarksCompatible
	// (DEFAULT_MARK = 0 would connect to anything).
	public static int NodeMark(this LongDistancePipeType type) => (int)type + 1;

	// Port of LongDistancePipeType.getMinLength() - the minimum straight-line
	// distance (in tiles, not pipe count) between two endpoints for the link to
	// work. Reads the per-type GTConfig value (default 50, matching upstream
	// ConfigHolder.machines.ld{Item,Fluid}PipeMinDistance).
	public static int MinLength(this LongDistancePipeType type)
	{
		var cfg = Config.GTConfig.Instance;
		return type == LongDistancePipeType.Item
			? cfg.LdItemPipeMinDistance
			: cfg.LdFluidPipeMinDistance;
	}
}
