#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.LongDistance;

// Per-cell payload for the unified long-distance pipe layer. The only state is
// the pipe Type (item vs fluid) - LD pipes have no covers, no per-side modes, no
// contents (the network is a flat connectivity graph; the wormhole transfer
// lives entirely on the endpoint machines). Two cells connect iff same Type.
public struct LongDistancePipeCell
{
	public LongDistancePipeType Type;

	public LongDistancePipeCell(LongDistancePipeType type)
	{
		Type = type;
	}
}
