#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// I/O side picker. None = off. Tile-Y grows downward: Up = -Y, Down = +Y.
public enum IODirection : byte
{
	None = 0,
	Up,
	Down,
	Left,
	Right,
}

// Per-machine auto-output config lives on AutoOutputTrait (verbatim upstream
// port). This file keeps only the 4-direction primitives used by the energy
// net and adjacency-push helpers.

public static class IODirectionExtensions
{
	public static (int dx, int dy) Offset(this IODirection d) => d switch
	{
		IODirection.Up    => (0, -1),
		IODirection.Down  => (0,  1),
		IODirection.Left  => (-1, 0),
		IODirection.Right => (1,  0),
		_                  => (0,  0),
	};

	// Forge Direction.getOpposite() analogue. Critical at the cable<->endpoint
	// boundary so side-filtering doesn't match the wrong cable. None stays None.
	public static IODirection Opposite(this IODirection d) => d switch
	{
		IODirection.Up    => IODirection.Down,
		IODirection.Down  => IODirection.Up,
		IODirection.Left  => IODirection.Right,
		IODirection.Right => IODirection.Left,
		_                  => IODirection.None,
	};
}

// Canonical cardinal-side iteration order. Up/Down/Left/Right matches
// upstream's NSWE order projected to 2D.
public static class MachineSides
{
	public static readonly System.Collections.Generic.IReadOnlyList<(IODirection side, int dx, int dy)>
		Cardinal4 = new (IODirection, int, int)[]
		{
			(IODirection.Up,    0, -1),
			(IODirection.Down,  0,  1),
			(IODirection.Left, -1,  0),
			(IODirection.Right, 1,  0),
		};
}
