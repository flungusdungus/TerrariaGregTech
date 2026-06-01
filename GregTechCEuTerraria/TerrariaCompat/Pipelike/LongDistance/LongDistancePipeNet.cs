#nullable enable
using GregTechCEuTerraria.Api.Pipenet;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.LongDistance;

// One connected component of LD pipe cells = upstream LongDistanceNetwork's
// `longDistancePipeBlocks` set. The PipeNet framework gives us merge-on-place
// (UniteNetworks) + split-on-remove (RebuildNetworkOnNodeRemoval) for free, so
// this class only supplies the (empty) node payload serialization. Endpoint
// pairing / the active input-output selection lives on LongDistanceEndpoint*
// (queried at runtime via Level.GetNetFromPos), not on the net instance - our
// endpoints are machines, not pipe cells, so they're not net nodes.
//
// Item vs fluid separation is enforced by Node.Mark (Item=1, Fluid=2): the
// framework's AreMarksCompatible only merges equal non-zero marks.
public sealed class LongDistancePipeNet : PipeNet<LongDistancePipeProperties>
{
	public LongDistancePipeNet(ILevelPipeNet<LongDistancePipeProperties> level) : base(level) { }

	// No per-node payload to persist; the shared INSTANCE is read back.
	protected override void WriteNodeData(LongDistancePipeProperties nodeData, TagCompound tag) { }
	protected override LongDistancePipeProperties ReadNodeData(TagCompound tag) => LongDistancePipeProperties.INSTANCE;
}
