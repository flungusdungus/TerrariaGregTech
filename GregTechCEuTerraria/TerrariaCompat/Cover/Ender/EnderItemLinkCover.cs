#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Cover.Filter;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Ender;

// Port of common.cover.ender.EnderItemLinkCover. 8 items/t through a virtual
// item buffer, filter-gated.
public class EnderItemLinkCover : AbstractEnderLinkCover<VirtualItemStorage>
{
	private const int TransferRate = 8;

	private VirtualItemStorage? _storage;
	private int _itemsLeftToTransferLastSecond = TransferRate * 20;
	protected readonly ItemFilterHandler FilterHandler;

	public override ItemFilterHandler? UiItemFilterHandler => FilterHandler;

	public EnderItemLinkCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide)
	{
		FilterHandler = FilterHandlers.Item(this);
	}

	protected override string Identifier() => "EILink#";
	protected override VirtualItemStorage? GetEntry() => _storage;
	protected override void SetEntry(VirtualEntry entry) => _storage = (VirtualItemStorage)entry;
	protected override EnderEntryType GetEntryType() => EnderEntryType.Item;

	protected override void Transfer()
	{
		if (_storage == null) return;
		if (_itemsLeftToTransferLastSecond > 0)
			_itemsLeftToTransferLastSecond -= DoTransferItems(_itemsLeftToTransferLastSecond);
		if (CoverHolder.GetOffsetTimer() % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(20) == 0)
			_itemsLeftToTransferLastSecond = TransferRate * 20;
	}

	private int DoTransferItems(int max)
	{
		var own = GetOwnItemHandler();
		if (own == null || _storage == null) return 0;
		var filter = FilterHandler.GetFilter();
		return Io switch
		{
			// IN: machine -> ender; OUT: ender -> machine.
			IO.IN  => EnderTransfer.TransferItems(own, _storage.Handler, filter, max),
			IO.OUT => EnderTransfer.TransferItems(_storage.Handler, own, filter, max),
			_      => 0,
		};
	}

	public override List<Item> GetAdditionalDrops()
	{
		var list = base.GetAdditionalDrops();
		if (!FilterHandler.FilterItem.IsAir) list.Add(FilterHandler.FilterItem);
		return list;
	}

	public override void Save(TagCompound tag)
	{
		base.Save(tag);
		var filterTag = new TagCompound();
		FilterHandler.Save(filterTag);
		tag["filter"] = filterTag;
	}

	public override void Load(TagCompound tag)
	{
		base.Load(tag);
		if (tag.ContainsKey("filter")) FilterHandler.Load(tag.GetCompound("filter"));
	}
}
