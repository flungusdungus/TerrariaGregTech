#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Cover.Filter;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Ender;

// Port of common.cover.ender.EnderFluidLinkCover. Fluid twin of EnderItemLink;
// 8000 mB/t through a virtual tank, filter-gated.
public class EnderFluidLinkCover : AbstractEnderLinkCover<VirtualTank>
{
	private const int TransferRate = 8000;

	private VirtualTank? _visualTank;
	private int _mBLeftToTransferLastSecond = TransferRate * 20;
	protected readonly FluidFilterHandler FilterHandler;

	public override FluidFilterHandler? UiFluidFilterHandler => FilterHandler;

	public EnderFluidLinkCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide)
	{
		FilterHandler = FilterHandlers.Fluid(this);
	}

	public override bool CanAttach() => base.CanAttach() && GetOwnFluidHandler() != null;

	protected override string Identifier() => "EFLink#";
	protected override VirtualTank? GetEntry() => _visualTank;
	protected override void SetEntry(VirtualEntry entry) => _visualTank = (VirtualTank)entry;
	protected override EnderEntryType GetEntryType() => EnderEntryType.Fluid;

	protected override void Transfer()
	{
		if (_visualTank == null) return;
		if (_mBLeftToTransferLastSecond > 0)
			_mBLeftToTransferLastSecond -= DoTransferFluids(_mBLeftToTransferLastSecond);
		if (CoverHolder.GetOffsetTimer() % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(20) == 0)
			_mBLeftToTransferLastSecond = TransferRate * 20;
	}

	private int DoTransferFluids(int max)
	{
		var own = GetOwnFluidHandler();
		if (own == null || _visualTank == null) return 0;
		var filter = FilterHandler.GetFilter();
		return Io switch
		{
			// IN: machine -> ender; OUT: ender -> machine.
			IO.IN  => EnderTransfer.TransferFluids(own, _visualTank.FluidTank, filter, max),
			IO.OUT => EnderTransfer.TransferFluids(_visualTank.FluidTank, own, filter, max),
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
