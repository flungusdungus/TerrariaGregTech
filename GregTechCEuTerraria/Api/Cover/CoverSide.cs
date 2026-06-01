#nullable enable
namespace GregTechCEuTerraria.Api.Cover;

// Adaptation of net.minecraft.core.Direction for cover attachment.
//
// Upstream attaches covers to all 6 world sides. Terraria is 2D - a machine
// has exactly 4 tile-neighbours and no front-facing - so we keep the 4
// cardinals. Used as the per-side index into ICoverable's cover array.
public enum CoverSide
{
	Up    = 0,
	Down  = 1,
	Left  = 2,
	Right = 3,
}

public static class CoverSides
{
	public const int Count = 4;

	public static readonly CoverSide[] All =
		{ CoverSide.Up, CoverSide.Down, CoverSide.Left, CoverSide.Right };

	public static CoverSide Opposite(CoverSide side) => side switch
	{
		CoverSide.Up    => CoverSide.Down,
		CoverSide.Down  => CoverSide.Up,
		CoverSide.Left  => CoverSide.Right,
		CoverSide.Right => CoverSide.Left,
		_               => side,
	};
}
