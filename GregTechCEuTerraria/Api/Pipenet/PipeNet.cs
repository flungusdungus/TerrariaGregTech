#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Cover;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Pipenet;

// Verbatim port of com.gregtechceu.gtceu.api.pipenet.PipeNet - the abstract
// per-network instance. Holds a sparse map of nodes by 2D position +
// per-chunk index for fast getNetFromPos. Subclasses (ItemPipeNet,
// FluidPipeNet, ...) override writeNodeData/readNodeData + the network-level
// callbacks (onPipeConnectionsUpdate, onNeighbourUpdate).
//
// Adaptations from upstream (already-established across this codebase):
//   - BlockPos -> (int x, int y) tuple
//   - Direction -> CoverSide (4 cardinal sides; bit positions match ordinals)
//   - ChunkPos -> (int cx, int cy) = (pos.x / 16, pos.y / 16) - same idea
//   - ServerLevel reference dropped (no per-level state to grab from here;
//     subclasses query Terraria.Main directly when needed)
public abstract class PipeNet<TData> where TData : notnull
{
	protected readonly ILevelPipeNet<TData> WorldData;
	private readonly Dictionary<(int x, int y), Node<TData>> _nodeByPos = new();
	private readonly Dictionary<(int cx, int cy), int> _ownedChunks = new();
	private long _lastUpdate;
	internal bool IsValidInternal;  // set by LevelPipeNet on add/remove

	public PipeNet(ILevelPipeNet<TData> level)
	{
		WorldData = level;
	}

	public IReadOnlyCollection<(int cx, int cy)> ContainedChunks => _ownedChunks.Keys;

	public ILevelPipeNet<TData> GetWorldData() => WorldData;

	public long LastUpdate => _lastUpdate;

	public bool IsValid => IsValidInternal;

	// Called on connection-only updates (open/closed sides flip).
	protected virtual void OnNodeConnectionsUpdate() { _lastUpdate = System.Environment.TickCount64; }

	// Called when a node's data field is replaced.
	protected virtual void OnNodeDataUpdate() { }

	// Called when any pipe in the net changes its block-level connections.
	public virtual void OnPipeConnectionsUpdate() { }

	public virtual void OnNeighbourUpdate((int x, int y) fromPos) { }

	public IReadOnlyDictionary<(int x, int y), Node<TData>> AllNodes => _nodeByPos;

	public Node<TData>? GetNodeAt((int x, int y) pos) =>
		_nodeByPos.TryGetValue(pos, out var n) ? n : null;

	public bool ContainsNode((int x, int y) pos) => _nodeByPos.ContainsKey(pos);

	public bool IsNodeConnectedTo((int x, int y) pos, CoverSide side)
	{
		var nodeFirst = GetNodeAt(pos);
		if (nodeFirst == null) return false;
		var offset = Offset(pos, side);
		var nodeSecond = GetNodeAt(offset);
		if (nodeSecond == null) return false;
		return CanNodesConnect(nodeFirst, side, nodeSecond, this);
	}

	protected void AddNodeSilently((int x, int y) pos, Node<TData> node)
	{
		_nodeByPos[pos] = node;
		CheckAddedInChunk(pos);
	}

	protected internal void AddNode((int x, int y) pos, Node<TData> node)
	{
		AddNodeSilently(pos, node);
		OnNodeConnectionsUpdate();
		WorldData.SetDirty();
	}

	protected Node<TData>? RemoveNodeWithoutRebuilding((int x, int y) pos)
	{
		if (!_nodeByPos.Remove(pos, out var removed)) return null;
		EnsureRemovedFromChunk(pos);
		WorldData.SetDirty();
		return removed;
	}

	public void RemoveNode((int x, int y) pos)
	{
		if (_nodeByPos.ContainsKey(pos))
		{
			var selfNode = RemoveNodeWithoutRebuilding(pos)!;
			RebuildNetworkOnNodeRemoval(pos, selfNode);
		}
	}

	protected void CheckAddedInChunk((int x, int y) pos)
	{
		var chunkPos = ToChunkPos(pos);
		int oldValue = _ownedChunks.TryGetValue(chunkPos, out var v) ? v : 0;
		_ownedChunks[chunkPos] = oldValue + 1;
		if (oldValue == 0 && IsValid)
			WorldData.AddPipeNetToChunk(chunkPos, this);
	}

	protected void EnsureRemovedFromChunk((int x, int y) pos)
	{
		var chunkPos = ToChunkPos(pos);
		if (!_ownedChunks.TryGetValue(chunkPos, out var oldValue)) return;
		if (oldValue == 1)
		{
			_ownedChunks.Remove(chunkPos);
			if (IsValid) WorldData.RemovePipeNetFromChunk(chunkPos, this);
		}
		else
		{
			_ownedChunks[chunkPos] = oldValue - 1;
		}
	}

	public void UpdateBlockedConnections((int x, int y) pos, CoverSide facing, bool isBlocked)
	{
		if (!ContainsNode(pos)) return;
		var selfNode = GetNodeAt(pos)!;
		if (selfNode.IsBlocked(facing) == isBlocked) return;

		SetBlocked(selfNode, facing, isBlocked);
		var offsetPos = Offset(pos, facing);
		var pipeNetAtOffset = WorldData.GetNetFromPos(offsetPos);
		if (pipeNetAtOffset == null) return;

		if (pipeNetAtOffset == this)
		{
			// Same net both sides - re-checking connectivity may split.
			if (isBlocked)
			{
				SetBlocked(selfNode, facing, false);
				if (CanNodesConnect(selfNode, facing, GetNodeAt(offsetPos)!, this))
				{
					SetBlocked(selfNode, facing, true);
					var thisENet = FindAllConnectedBlocks(pos);
					if (!DictsEqual(AllNodes, thisENet))
					{
						var newPipeNet = WorldData.CreateNetInstance();
						foreach (var k in thisENet.Keys) RemoveNodeWithoutRebuilding(k);
						newPipeNet.TransferNodeData(thisENet, this);
						WorldData.AddPipeNet(newPipeNet);
					}
				}
			}
		}
		else if (!isBlocked)
		{
			var neighbourNode = pipeNetAtOffset.GetNodeAt(offsetPos)!;
			if (CanNodesConnect(selfNode, facing, neighbourNode, pipeNetAtOffset) &&
				pipeNetAtOffset.CanNodesConnect(neighbourNode, CoverSides.Opposite(facing), selfNode, this))
			{
				UniteNetworks(pipeNetAtOffset);
			}
		}
		OnNodeConnectionsUpdate();
		WorldData.SetDirty();
	}

	public void UpdateNodeData((int x, int y) pos, TData data)
	{
		if (ContainsNode(pos))
		{
			var selfNode = GetNodeAt(pos)!;
			selfNode.Data = data;
			OnNodeDataUpdate();
			WorldData.SetDirty();
		}
	}

	public void UpdateMark((int x, int y) pos, int newMark)
	{
		if (!ContainsNode(pos)) return;
		Dictionary<(int x, int y), Node<TData>>? selfConnectedBlocks = null;
		var selfNode = GetNodeAt(pos)!;
		int oldMark = selfNode.Mark;
		selfNode.Mark = newMark;
		foreach (var facing in CoverSides.All)
		{
			var offsetPos = Offset(pos, facing);
			var otherPipeNet = WorldData.GetNetFromPos(offsetPos);
			var secondNode = otherPipeNet?.GetNodeAt(offsetPos);
			if (secondNode == null) continue;
			if (!AreNodeBlockedConnectionsCompatible(selfNode, facing, secondNode) ||
				!AreNodesCustomContactable(selfNode.Data, secondNode.Data, otherPipeNet!))
				continue;
			if (AreMarksCompatible(oldMark, secondNode.Mark) == AreMarksCompatible(newMark, secondNode.Mark))
				continue;
			if (AreMarksCompatible(newMark, secondNode.Mark))
			{
				if (otherPipeNet != this) UniteNetworks(otherPipeNet!);
			}
			else if (otherPipeNet == this)
			{
				selfConnectedBlocks ??= FindAllConnectedBlocks(pos);
				if (DictsEqual(AllNodes, selfConnectedBlocks)) continue;
				var offsetConnectedBlocks = FindAllConnectedBlocks(offsetPos);
				if (!DictsEqual(offsetConnectedBlocks, selfConnectedBlocks))
				{
					foreach (var k in offsetConnectedBlocks.Keys) RemoveNodeWithoutRebuilding(k);
					var offsetPipeNet = WorldData.CreateNetInstance();
					offsetPipeNet.TransferNodeData(offsetConnectedBlocks, this);
					WorldData.AddPipeNet(offsetPipeNet);
				}
			}
		}
		OnNodeConnectionsUpdate();
		WorldData.SetDirty();
	}

	private static void SetBlocked(Node<TData> selfNode, CoverSide facing, bool isBlocked)
	{
		if (!isBlocked) selfNode.OpenConnections |=  (1 << (int)facing);
		else            selfNode.OpenConnections &= ~(1 << (int)facing);
	}

	public bool MarkNodeAsActive((int x, int y) pos, bool isActive)
	{
		var node = GetNodeAt(pos);
		if (node != null && node.IsActive != isActive)
		{
			node.IsActive = isActive;
			WorldData.SetDirty();
			OnNodeConnectionsUpdate();
			return true;
		}
		return false;
	}

	protected internal void UniteNetworks(PipeNet<TData> united)
	{
		var allNodes = new Dictionary<(int x, int y), Node<TData>>(united.AllNodes);
		WorldData.RemovePipeNet(united);
		foreach (var k in allNodes.Keys) united.RemoveNodeWithoutRebuilding(k);
		TransferNodeData(allNodes, united);
	}

	private static bool AreNodeBlockedConnectionsCompatible(Node<TData> first, CoverSide firstFacing, Node<TData> second) =>
		!first.IsBlocked(firstFacing) && !second.IsBlocked(CoverSides.Opposite(firstFacing));

	private static bool AreMarksCompatible(int m1, int m2) =>
		m1 == m2 || m1 == Node<TData>.DEFAULT_MARK || m2 == Node<TData>.DEFAULT_MARK;

	protected internal bool CanNodesConnect(Node<TData> first, CoverSide firstFacing, Node<TData> second,
		PipeNet<TData>? secondPipeNet) =>
		AreNodeBlockedConnectionsCompatible(first, firstFacing, second) &&
		AreMarksCompatible(first.Mark, second.Mark) &&
		AreNodesCustomContactable(first.Data, second.Data, secondPipeNet);

	protected Dictionary<(int x, int y), Node<TData>> FindAllConnectedBlocks((int x, int y) startPos)
	{
		var observedSet = new Dictionary<(int x, int y), Node<TData>>
		{
			[startPos] = GetNodeAt(startPos)!
		};
		var firstNode = GetNodeAt(startPos)!;
		var currentPos = startPos;
		var moveStack = new Stack<CoverSide>();
		while (true)
		{
			bool advanced = false;
			foreach (var facing in CoverSides.All)
			{
				var nextPos = Offset(currentPos, facing);
				var secondNode = GetNodeAt(nextPos);
				if (secondNode != null && CanNodesConnect(firstNode, facing, secondNode, this) &&
					!observedSet.ContainsKey(nextPos))
				{
					observedSet[nextPos] = secondNode;
					firstNode = secondNode;
					currentPos = nextPos;
					moveStack.Push(CoverSides.Opposite(facing));
					advanced = true;
					break;
				}
			}
			if (advanced) continue;
			if (moveStack.Count > 0)
			{
				currentPos = Offset(currentPos, moveStack.Pop());
				firstNode = GetNodeAt(currentPos)!;
			}
			else break;
		}
		return observedSet;
	}

	protected void RebuildNetworkOnNodeRemoval((int x, int y) pos, Node<TData> selfNode)
	{
		int amountOfConnectedSides = 0;
		foreach (var facing in CoverSides.All)
		{
			if (ContainsNode(Offset(pos, facing))) amountOfConnectedSides++;
		}
		if (amountOfConnectedSides >= 2)
		{
			foreach (var facing in CoverSides.All)
			{
				var offsetPos = Offset(pos, facing);
				var secondNode = GetNodeAt(offsetPos);
				if (secondNode == null || !CanNodesConnect(selfNode, facing, secondNode, this)) continue;
				var thisENet = FindAllConnectedBlocks(offsetPos);
				if (DictsEqual(AllNodes, thisENet)) break;
				var energyNet = WorldData.CreateNetInstance();
				foreach (var k in thisENet.Keys) RemoveNodeWithoutRebuilding(k);
				energyNet.TransferNodeData(thisENet, this);
				WorldData.AddPipeNet(energyNet);
			}
		}
		if (AllNodes.Count == 0) WorldData.RemovePipeNet(this);
		OnNodeConnectionsUpdate();
		WorldData.SetDirty();
	}

	protected virtual bool AreNodesCustomContactable(TData first, TData second, PipeNet<TData>? secondNodePipeNet) => true;

	protected internal virtual bool CanAttachNode(TData nodeData) => true;

	// Called during a net split. Default implementation: copy all nodes into
	// this net. Subclasses override to also transfer per-net state (fluid
	// tank contents, etc.).
	protected internal virtual void TransferNodeData(
		Dictionary<(int x, int y), Node<TData>> transferredNodes,
		PipeNet<TData> parentNet)
	{
		foreach (var kv in transferredNodes) AddNodeSilently(kv.Key, kv.Value);
		OnNodeConnectionsUpdate();
		WorldData.SetDirty();
	}

	// -- NBT (TagCompound) serialization - verbatim shape against upstream --
	protected abstract void WriteNodeData(TData nodeData, TagCompound tagCompound);
	protected abstract TData ReadNodeData(TagCompound tagCompound);

	public TagCompound SerializeNBT()
	{
		var compound = new TagCompound();
		compound["Nodes"] = SerializeAllNodeList(_nodeByPos);
		return compound;
	}

	public void DeserializeNBT(TagCompound nbt)
	{
		_nodeByPos.Clear();
		_ownedChunks.Clear();
		if (nbt.ContainsKey("Nodes")) DeserializeAllNodeList(nbt.GetCompound("Nodes"));
	}

	private void DeserializeAllNodeList(TagCompound compound)
	{
		var allNodesList    = compound.GetList<TagCompound>("NodeIndexes");
		var wirePropsList   = compound.GetList<TagCompound>("WireProperties");
		var readProperties  = new Dictionary<int, TData>();
		foreach (var pt in wirePropsList)
		{
			int idx = pt.GetInt("index");
			readProperties[idx] = ReadNodeData(pt);
		}
		foreach (var nt in allNodesList)
		{
			int x = nt.GetInt("x");
			int y = nt.GetInt("y");
			int wpIdx = nt.GetInt("index");
			var nodeData = readProperties[wpIdx];
			int openConnections = nt.ContainsKey("open")   ? nt.GetInt("open")        : Node<TData>.ALL_OPENED;
			int mark            = nt.ContainsKey("mark")   ? nt.GetInt("mark")        : Node<TData>.DEFAULT_MARK;
			bool isActive       = nt.ContainsKey("active") && nt.GetBool("active");
			AddNodeSilently((x, y), new Node<TData>(nodeData, openConnections, mark, isActive));
		}
	}

	private TagCompound SerializeAllNodeList(IReadOnlyDictionary<(int x, int y), Node<TData>> allNodes)
	{
		var compound = new TagCompound();
		var allNodesList  = new List<TagCompound>();
		var wirePropsList = new List<TagCompound>();
		// Identity-based dedup so equal NodeDataType instances share one
		// payload index (mirrors upstream's Object2IntOpenHashMap usage).
		var alreadyWritten = new Dictionary<TData, int>(EqualityComparer<TData>.Default);
		int currentIndex = 0;

		foreach (var (nodePos, node) in allNodes)
		{
			var nodeTag = new TagCompound();
			nodeTag["x"] = nodePos.x;
			nodeTag["y"] = nodePos.y;
			if (!alreadyWritten.TryGetValue(node.Data, out int wpIdx))
			{
				wpIdx = currentIndex++;
				alreadyWritten[node.Data] = wpIdx;
			}
			nodeTag["index"] = wpIdx;
			if (node.Mark != Node<TData>.DEFAULT_MARK) nodeTag["mark"] = node.Mark;
			if (node.OpenConnections > 0)              nodeTag["open"] = node.OpenConnections;
			if (node.IsActive)                          nodeTag["active"] = true;
			allNodesList.Add(nodeTag);
		}

		foreach (var (data, wpIdx) in alreadyWritten)
		{
			var pt = new TagCompound();
			pt["index"] = wpIdx;
			WriteNodeData(data, pt);
			wirePropsList.Add(pt);
		}

		compound["NodeIndexes"]    = allNodesList;
		compound["WireProperties"] = wirePropsList;
		return compound;
	}

	// -- Helpers --------------------------------------------------------
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

	private static bool DictsEqual(
		IReadOnlyDictionary<(int x, int y), Node<TData>> a,
		IReadOnlyDictionary<(int x, int y), Node<TData>> b)
	{
		if (a.Count != b.Count) return false;
		foreach (var k in a.Keys) if (!b.ContainsKey(k)) return false;
		return true;
	}
}
