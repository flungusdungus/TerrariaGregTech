#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Data.Chemical.Material.Properties;
using GregTechCEuTerraria.Api.Pipenet;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.ItemPipe;

// Verbatim port of ItemPipeNet. Three route caches per source pipe
// (Full / NonRestricted / Restricted); first GetNetData populates all
// three, topology changes clear them all.
public sealed class ItemPipeNet : PipeNet<ItemPipeProperties>
{
	private readonly Dictionary<(int x, int y), List<ItemRoutePath>> _netDataFull         = new();
	private readonly Dictionary<(int x, int y), List<ItemRoutePath>> _netDataNonRestrict  = new();
	private readonly Dictionary<(int x, int y), List<ItemRoutePath>> _netDataOnlyRestrict = new();

	public ItemPipeNet(ILevelPipeNet<ItemPipeProperties> level) : base(level) { }

	public List<ItemRoutePath> GetNetData((int x, int y) pipePos, CoverSide facing, ItemRoutePathSet set)
	{
		Dictionary<(int x, int y), List<ItemRoutePath>> bucket = set switch
		{
			ItemRoutePathSet.Full          => _netDataFull,
			ItemRoutePathSet.NonRestricted => _netDataNonRestrict,
			ItemRoutePathSet.Restricted    => _netDataOnlyRestrict,
			_                              => _netDataFull,
		};
		if (bucket.TryGetValue(pipePos, out var cached)) return cached;

		var data = ItemNetWalker.CreateNetData(this, pipePos, facing);
		if (data is null)
		{
			// Walker failed - don't cache so insertion retries next call.
			return new List<ItemRoutePath>();
		}
		data.Sort((a, b) => a.Properties.Priority.CompareTo(b.Properties.Priority));

		var nonRestricted = new List<ItemRoutePath>();
		var restricted    = new List<ItemRoutePath>();
		foreach (var route in data)
		{
			if (route.Restrictive) restricted.Add(route);
			else                    nonRestricted.Add(route);
		}

		_netDataFull        [pipePos] = data;
		_netDataNonRestrict [pipePos] = nonRestricted;
		_netDataOnlyRestrict[pipePos] = restricted;

		return set switch
		{
			ItemRoutePathSet.Full          => data,
			ItemRoutePathSet.NonRestricted => nonRestricted,
			ItemRoutePathSet.Restricted    => restricted,
			_                              => data,
		};
	}

	public override void OnNeighbourUpdate((int x, int y) fromPos) => ClearNetData();

	public override void OnPipeConnectionsUpdate() => ClearNetData();

	protected internal override void TransferNodeData(
		Dictionary<(int x, int y), Node<ItemPipeProperties>> transferredNodes,
		PipeNet<ItemPipeProperties> parentNet)
	{
		base.TransferNodeData(transferredNodes, parentNet);
		ClearNetData();
		if (parentNet is ItemPipeNet parent) parent.ClearNetData();
	}

	private void ClearNetData()
	{
		_netDataFull.Clear();
		_netDataNonRestrict.Clear();
		_netDataOnlyRestrict.Clear();
	}

	protected override void WriteNodeData(ItemPipeProperties nodeData, TagCompound tag)
	{
		tag["Resistance"] = nodeData.Priority;
		tag["Rate"]       = nodeData.TransferRate;
	}

	protected override ItemPipeProperties ReadNodeData(TagCompound tag) =>
		new(tag.GetInt("Resistance"), tag.GetFloat("Rate"));
}
