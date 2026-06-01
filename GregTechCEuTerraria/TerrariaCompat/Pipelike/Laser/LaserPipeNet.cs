#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Pipenet;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Laser;

// Verbatim port of com.gregtechceu.gtceu.common.pipelike.laser.LaserPipeNet.
//
// Per-net route cache. Each (sourcePipe, sourceFacing) tuple resolves to one
// LaserRoutePath via the LaserNetWalker. Topology / neighbour updates clear
// the cache so the next read re-walks. Verbatim with upstream behaviour:
//   - `getNetData(pipePos, facing)` looks up the cache; on miss, walks and
//     caches (or returns null if the walker FAILED_MARKERed it).
//   - `onNeighbourUpdate` + `onPipeConnectionsUpdate` both clear the cache
//     (any topology change can re-route a laser line).
//   - `readNodeData` returns the shared `INSTANCE` (no per-node payload).
public sealed class LaserPipeNet : PipeNet<LaserPipeProperties>
{
	private readonly Dictionary<((int x, int y) pipe, IODirection face), LaserRoutePath> _netData = new();

	public LaserPipeNet(ILevelPipeNet<LaserPipeProperties> level) : base(level) { }

	public LaserRoutePath? GetNetData((int x, int y) pipePos, IODirection facing)
	{
		var key = (pipePos, facing);
		if (_netData.TryGetValue(key, out var data)) return data;

		var fresh = LaserNetWalker.CreateNetData(this, pipePos, facing);
		if (fresh == LaserNetWalker.FAILED_MARKER)
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
	protected override void WriteNodeData(LaserPipeProperties nodeData, TagCompound tag) { }
	protected override LaserPipeProperties ReadNodeData(TagCompound tag) => LaserPipeProperties.INSTANCE;
}
