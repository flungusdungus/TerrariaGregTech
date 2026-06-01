#nullable enable
namespace GregTechCEuTerraria.Api.Cover.Data;

// Port of com.gregtechceu.gtceu.common.cover.data.ControllerMode - what a
// machine-controller cover drives: the host machine itself, or a cover on one
// of its sides.
//
// Adaptation: upstream has 6 COVER_<dir> values (a Direction each). Terraria
// is 2D - we keep the 4 cardinal sides, matching CoverSide.
public enum ControllerMode
{
	Machine,
	CoverUp,
	CoverDown,
	CoverLeft,
	CoverRight,
}
