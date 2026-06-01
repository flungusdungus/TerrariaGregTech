#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Pipenet;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Optical;

// Verbatim port of com.gregtechceu.gtceu.common.pipelike.optical.OpticalPipeNet.
//
// Per-net route cache, identical shape to LaserPipeNet. Each
// (sourcePipe, sourceFacing) tuple resolves to one OpticalRoutePath via the
// OpticalNetWalker. Topology / neighbour updates clear the cache so the next
// read re-walks.
public sealed class OpticalPipeNet : PipeNet<OpticalPipeProperties>
{
	private readonly Dictionary<((int x, int y) pipe, IODirection face), OpticalRoutePath> _netData = new();

	public OpticalPipeNet(ILevelPipeNet<OpticalPipeProperties> level) : base(level) { }

	public OpticalRoutePath? GetNetData((int x, int y) pipePos, IODirection facing)
	{
		var key = (pipePos, facing);
		if (_netData.TryGetValue(key, out var data)) return data;

		var fresh = OpticalNetWalker.CreateNetData(this, pipePos, facing);
		if (fresh == OpticalNetWalker.FAILED_MARKER)
		{
			// walker failed, don't cache, so it tries again on next call
			return null;
		}
		if (fresh != null)
			_netData[key] = fresh;
		return fresh;
	}

	public override void OnNeighbourUpdate((int x, int y) fromPos) => _netData.Clear();
	public override void OnPipeConnectionsUpdate()                  => _netData.Clear();

	// No per-node payload to persist; the shared INSTANCE is read back.
	protected override void WriteNodeData(OpticalPipeProperties nodeData, TagCompound tag) { }
	protected override OpticalPipeProperties ReadNodeData(TagCompound tag) => OpticalPipeProperties.INSTANCE;
}
