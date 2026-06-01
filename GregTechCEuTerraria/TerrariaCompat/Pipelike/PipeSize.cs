#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Pipelike;

// Pipe-size discriminator. Upstream ships:
//   Fluid:  tiny / small / normal / large / huge / quadruple / nonuple
//   Item:   small / normal / large / huge (+ restrictive)
public enum PipeSize : byte
{
	Tiny       = 1,
	Small      = 2,
	Normal     = 3,
	Large      = 4,
	Huge       = 5,
	Quadruple  = 6,
	Nonuple    = 7,
}

public static class PipeSizes
{
	// Matches dump prefix + `pipe_<size>_in.png` texture filename.
	public static string Word(PipeSize size) => size switch
	{
		PipeSize.Tiny      => "tiny",
		PipeSize.Small     => "small",
		PipeSize.Normal    => "normal",
		PipeSize.Large     => "large",
		PipeSize.Huge      => "huge",
		PipeSize.Quadruple => "quadruple",
		PipeSize.Nonuple   => "nonuple",
		_                  => "normal",
	};

	public static PipeSize FromWord(string word) => word switch
	{
		"tiny"      => PipeSize.Tiny,
		"small"     => PipeSize.Small,
		"normal"    => PipeSize.Normal,
		"large"     => PipeSize.Large,
		"huge"      => PipeSize.Huge,
		"quadruple" => PipeSize.Quadruple,
		"nonuple"   => PipeSize.Nonuple,
		_           => PipeSize.Normal,
	};

	// Verbatim upstream FluidPipeType.capacityMultiplier.
	public static int FluidPipeCapacityMultiplier(PipeSize size) => size switch
	{
		PipeSize.Tiny      => 1,
		PipeSize.Small     => 2,
		PipeSize.Normal    => 6,
		PipeSize.Large     => 12,
		PipeSize.Huge      => 24,
		PipeSize.Quadruple => 2,
		PipeSize.Nonuple   => 2,
		_                  => 1,
	};

	// Verbatim upstream FluidPipeType.channels.
	public static int FluidPipeChannels(PipeSize size) => size switch
	{
		PipeSize.Quadruple => 4,
		PipeSize.Nonuple   => 9,
		_                  => 1,
	};
}
