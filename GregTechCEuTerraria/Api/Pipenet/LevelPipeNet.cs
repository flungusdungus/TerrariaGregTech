#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Cover;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Pipenet;

// Verbatim port of com.gregtechceu.gtceu.api.pipenet.LevelPipeNet -
// the per-level container of PipeNet instances. Drives add / remove /
// merge / split lifecycle. Subclasses (ItemPipeNetSystem, FluidPipeNetSystem)
// provide `CreateNetInstance` and host an instance from a ModSystem.
//
// Adaptations:
//   - Generic on `TNet` covariance dropped - kept the constraint but use a
//     plain List<TNet> like upstream.
//   - SavedData replaced with explicit Save/Load (called from the host
//     ModSystem's SaveWorldData / LoadWorldData).
//   - ServerLevel reference dropped (we have no analogue).
public abstract class LevelPipeNet<TData, TNet> : ILevelPipeNet<TData>
	where TData : notnull
	where TNet  : PipeNet<TData>
{
	protected List<TNet> PipeNets = new();
	protected readonly Dictionary<(int cx, int cy), List<TNet>> PipeNetsByChunk = new();

	// Dirty flag - upstream's `setDirty()` schedules a save. Hosts inspect
	// + clear this whenever they emit save data.
	private bool _dirty;
	public bool IsDirty => _dirty;
	public void SetDirty() => _dirty = true;
	public void ClearDirty() => _dirty = false;

	protected virtual void Init()
	{
		// Mirror upstream's `pipeNets.forEach(PipeNet::onNodeConnectionsUpdate)`
		// via the protected hook - subclasses can extend if they need per-net
		// post-load fix-up.
	}

	public void AddNode((int x, int y) pos, TData nodeData, int mark, int openConnections, bool isActive)
	{
		TNet? myPipeNet = null;
		var node = new Node<TData>(nodeData, openConnections, mark, isActive);
		foreach (var facing in CoverSides.All)
		{
			var offsetPos = Offset(pos, facing);
			var pipeNet = GetNetFromPos(offsetPos);
			var secondNode = pipeNet?.GetNodeAt(offsetPos);
			// Upstream-verbatim: pass null as `secondPipeNet` - the new
			// `node` isn't in any net yet, so there's nothing to identify.
			// `areNodesCustomContactable` overrides that consult the second
			// net must handle null on the add path.
			if (pipeNet != null && pipeNet.CanAttachNode(nodeData) &&
				pipeNet.CanNodesConnect(secondNode!, CoverSides.Opposite(facing), node, null))
			{
				if (myPipeNet == null)
				{
					myPipeNet = pipeNet;
					AsBaseNet(myPipeNet)!.AddNode(pos, node);
				}
				else if (!ReferenceEquals(myPipeNet, pipeNet))
				{
					myPipeNet.UniteNetworks(pipeNet);
				}
			}
		}
		if (myPipeNet == null)
		{
			myPipeNet = CreateNetInstance();
			AsBaseNet(myPipeNet)!.AddNode(pos, node);
			AddPipeNet(myPipeNet);
			SetDirty();
		}
	}

	public void AddPipeNetToChunk((int cx, int cy) chunkPos, PipeNet<TData> pipeNet)
	{
		if (!PipeNetsByChunk.TryGetValue(chunkPos, out var list))
			PipeNetsByChunk[chunkPos] = list = new List<TNet>();
		list.Add((TNet)pipeNet);
	}

	public void RemovePipeNetFromChunk((int cx, int cy) chunkPos, PipeNet<TData> pipeNet)
	{
		if (PipeNetsByChunk.TryGetValue(chunkPos, out var list))
		{
			list.Remove((TNet)pipeNet);
			if (list.Count == 0) PipeNetsByChunk.Remove(chunkPos);
		}
	}

	public void RemoveNode((int x, int y) pos)
	{
		var pipeNet = GetNetFromPos(pos);
		AsBaseNet(pipeNet)?.RemoveNode(pos);
	}

	public void UpdateBlockedConnections((int x, int y) pos, CoverSide side, bool isBlocked)
	{
		var pipeNet = GetNetFromPos(pos);
		if (pipeNet == null) return;
		AsBaseNet(pipeNet)!.UpdateBlockedConnections(pos, side, isBlocked);
		AsBaseNet(pipeNet)!.OnPipeConnectionsUpdate();
	}

	public void UpdateData((int x, int y) pos, TData data)
	{
		var pipeNet = GetNetFromPos(pos);
		AsBaseNet(pipeNet)?.UpdateNodeData(pos, data);
	}

	public void UpdateMark((int x, int y) pos, int newMark)
	{
		var pipeNet = GetNetFromPos(pos);
		AsBaseNet(pipeNet)?.UpdateMark(pos, newMark);
	}

	public TNet? GetNetFromPos((int x, int y) pos)
	{
		if (!PipeNetsByChunk.TryGetValue(ToChunkPos(pos), out var list)) return null;
		foreach (var pn in list)
			if (pn.ContainsNode(pos)) return pn;
		return null;
	}

	// Interface-required base-typed surface - return-type covariance lets
	// the TNet-typed property above remain available to direct callers.
	PipeNet<TData>? ILevelPipeNet<TData>.GetNetFromPos((int x, int y) pos) => GetNetFromPos(pos);

	public IReadOnlyList<TNet> AllPipeNets => PipeNets;

	public void AddPipeNet(PipeNet<TData> pipeNet) => AddPipeNetSilently((TNet)pipeNet);

	internal void AddPipeNetSilently(TNet pipeNet)
	{
		PipeNets.Add(pipeNet);
		foreach (var chunkPos in pipeNet.ContainedChunks)
			AddPipeNetToChunk(chunkPos, pipeNet);
		pipeNet.IsValidInternal = true;
	}

	public void RemovePipeNet(PipeNet<TData> pipeNet)
	{
		PipeNets.Remove((TNet)pipeNet);
		foreach (var chunkPos in pipeNet.ContainedChunks)
			RemovePipeNetFromChunk(chunkPos, pipeNet);
		pipeNet.IsValidInternal = false;
		SetDirty();
	}

	// ILevelPipeNet's CreateNetInstance returns the base type; the
	// concrete TNet variant stays on the subclass.
	PipeNet<TData> ILevelPipeNet<TData>.CreateNetInstance() => CreateNetInstance();

	// Each subclass returns a fresh per-net instance - caller wires up the
	// LevelPipeNet ref (the subclass picks how, since C# can't pass `this`
	// generically to a parameterized constructor).
	protected internal abstract TNet CreateNetInstance();

	// -- NBT save / load (called from host ModSystem) -------------------
	public TagCompound Save()
	{
		var compound = new TagCompound();
		var allPipeNets = new List<TagCompound>(PipeNets.Count);
		foreach (var pipeNet in PipeNets)
			allPipeNets.Add(pipeNet.SerializeNBT());
		compound["PipeNets"] = allPipeNets;
		_dirty = false;
		return compound;
	}

	public void Load(TagCompound tag)
	{
		PipeNets.Clear();
		PipeNetsByChunk.Clear();
		if (!tag.ContainsKey("PipeNets")) return;
		var allPipeNets = tag.GetList<TagCompound>("PipeNets");
		foreach (var pNetTag in allPipeNets)
		{
			var pipeNet = CreateNetInstance();
			pipeNet.DeserializeNBT(pNetTag);
			AddPipeNetSilently(pipeNet);
		}
		Init();
	}

	// TNet IS-A PipeNet<TData>; this adapter keeps the upcasts in one place
	// so the call sites read like the upstream Java without explicit casts.
	private static PipeNet<TData>? AsBaseNet(TNet? n) => n;

	// -- Helpers -------------------------------------------------------
	private static (int x, int y) Offset((int x, int y) pos, CoverSide side)
	{
		var (dx, dy) = side switch
		{
			CoverSide.Up    => (0, -1),
			CoverSide.Down  => (0, +1),
			CoverSide.Left  => (-1, 0),
			CoverSide.Right => (+1, 0),
			_               => (0, 0),
		};
		return (pos.x + dx, pos.y + dy);
	}

	private static (int cx, int cy) ToChunkPos((int x, int y) pos) => (pos.x >> 4, pos.y >> 4);
}
