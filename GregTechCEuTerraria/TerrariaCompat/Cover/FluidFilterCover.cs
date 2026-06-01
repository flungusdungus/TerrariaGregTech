#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Cover.Data;
using GregTechCEuTerraria.Api.Cover.Filter;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Transfer;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover;

// Port of common.cover.FluidFilterCover - fluid mirror of ItemFilterCover; see
// that file for the family/AttachItem design.
public class FluidFilterCover : CoverBehavior, IUICover
{
	private IFluidFilter? _fluidFilter;
	private TagCompound? _filterConfig;
	// DEVIATION: upstream defaults to
	// FILTER_INSERT, but the pipe-UI default is FilterBoth so a fresh
	// filter cover gates both directions out of the box.
	private FilterMode _filterMode = FilterMode.FilterBoth;
	private ManualIOMode _allowFlow = ManualIOMode.Disabled;
	private FilteredFluidHandlerWrapper? _fluidFilterWrapper;

	public FluidFilterCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide) { }

	public IFluidFilter GetFluidFilter()
	{
		if (_fluidFilter is null)
			_fluidFilter = FilterItemRegistry.LoadFluidFilter(AttachItem.type, _filterConfig ?? new TagCompound());
		return _fluidFilter;
	}

	public override SimpleFluidFilter? UiFluidFilter   => GetFluidFilter() as SimpleFluidFilter;
	public override TagFluidFilter?    UiTagFluidFilter => GetFluidFilter() as TagFluidFilter;

	public FilterMode FilterMode => _filterMode;
	public ManualIOMode AllowFlow => _allowFlow;

	public void SetFilterMode(FilterMode filterMode) => _filterMode = filterMode;
	public void SetAllowFlow(ManualIOMode allowFlow) => _allowFlow = allowFlow;

	public override void ApplySetting(int field, long value)
	{
		switch (field)
		{
			case 10: SetFilterMode((FilterMode)System.Math.Clamp(value, 0, 2)); break;
			case 11: SetAllowFlow((ManualIOMode)System.Math.Clamp(value, 0, 2)); break;
			default: base.ApplySetting(field, value); break;
		}
	}

	public override void OnAttached(Item itemStack)
	{
		base.OnAttached(itemStack);
		_fluidFilter = null;   // force re-dispatch via FilterItemRegistry off new AttachItem
	}

	public override bool CanAttach() => base.CanAttach() && CoverHolder is IFluidHandler;

	public override IFluidHandler? GetFluidHandlerCap(IFluidHandler? defaultValue)
	{
		if (defaultValue == null) return null;
		if (_fluidFilterWrapper == null || _fluidFilterWrapper.Inner != defaultValue)
			_fluidFilterWrapper = new FilteredFluidHandlerWrapper(this, defaultValue);
		return _fluidFilterWrapper;
	}

	private sealed class FilteredFluidHandlerWrapper : FluidHandlerDelegate
	{
		private readonly FluidFilterCover _cover;

		public FilteredFluidHandlerWrapper(FluidFilterCover cover, IFluidHandler inner) : base(inner) => _cover = cover;

		public override int Fill(FluidStack resource, bool simulate)
		{
			if (_cover._filterMode == FilterMode.FilterExtract)
			{
				if (_cover._allowFlow == ManualIOMode.Disabled) return 0;
				if (_cover._allowFlow == ManualIOMode.Unfiltered) return base.Fill(resource, simulate);
			}
			if (!_cover.GetFluidFilter().Test(resource)) return 0;
			return base.Fill(resource, simulate);
		}

		public override FluidStack Drain(FluidStack resource, bool simulate)
		{
			if (_cover._filterMode == FilterMode.FilterInsert)
			{
				if (_cover._allowFlow == ManualIOMode.Disabled) return FluidStack.Empty;
				if (_cover._allowFlow == ManualIOMode.Unfiltered) return base.Drain(resource, simulate);
			}
			if (!_cover.GetFluidFilter().Test(resource)) return FluidStack.Empty;
			return base.Drain(resource, simulate);
		}
	}

	public override void Save(TagCompound tag)
	{
		base.Save(tag);
		tag["filterMode"] = (int)_filterMode;
		tag["manualIO"] = (int)_allowFlow;
		if (_fluidFilter is IFilter<FluidStack> f)
		{
			var cfg = f.SaveFilter();
			if (cfg is not null) _filterConfig = cfg;
		}
		if (_filterConfig is not null) tag["filterConfig"] = _filterConfig;
	}

	public override void Load(TagCompound tag)
	{
		base.Load(tag);
		if (tag.ContainsKey("filterMode")) _filterMode = (FilterMode)tag.GetInt("filterMode");
		if (tag.ContainsKey("manualIO")) _allowFlow = (ManualIOMode)tag.GetInt("manualIO");
		_filterConfig = tag.ContainsKey("filterConfig") ? tag.GetCompound("filterConfig") : null;
		_fluidFilter = null;
	}
}
