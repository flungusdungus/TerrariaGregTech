#nullable enable
namespace GregTechCEuTerraria.Api.Pipenet;

// Port of the long-distance pipe node payload. Upstream's LongDistanceNetwork
// stores no per-pipe data (the network is a flat pos set), so the node payload
// is an empty singleton - same shape as LaserPipeProperties. The item-vs-fluid
// distinction is carried by Node.Mark (1 = item, 2 = fluid) so the PipeNet
// framework keeps the two types in separate connected components automatically.
public sealed class LongDistancePipeProperties
{
	public static readonly LongDistancePipeProperties INSTANCE = new();
	private LongDistancePipeProperties() { }
}
