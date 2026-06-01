#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Cover.Data;
using GregTechCEuTerraria.Api.Cover.Filter;
using GregTechCEuTerraria.Api.Transfer;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover;

// Port of common.cover.ItemFilterCover - UPSTREAM-FAITHFUL. One CoverDefinition
// (GTCovers.ITEM_FILTER) for the whole family; Simple/Tag/Smart are different
// AttachItems that pick the concrete filter via FilterItemRegistry. The cover's
// filter is fixed once AttachItem is set - the pipe-UI's None/Simple/Tag
// toggle replaces the cover INSTANCE (PipeCoverable + CoverFilterAction).
//
// Adaptation: Terraria items have no NBT bag, so per-filter config is in the
// cover's own Save/Load blob as `filterConfig` (vs upstream's attachItem.NBT).
public class ItemFilterCover : CoverBehavior, IUICover
{
	private IItemFilter? _itemFilter;
	private TagCompound? _filterConfig;
	// DEVIATION: upstream defaults to
	// FILTER_INSERT, but the pipe-UI default is FilterBoth so a fresh
	// filter cover gates both directions out of the box.
	private FilterMode _filterMode = FilterMode.FilterBoth;
	private ManualIOMode _allowFlow = ManualIOMode.Disabled;
	private FilteredItemHandlerWrapper? _itemFilterWrapper;

	public ItemFilterCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide) { }

	// Verbatim getItemFilter - lazy load via FILTERS dispatch on AttachItem.
	public IItemFilter GetItemFilter()
	{
		if (_itemFilter is null)
			_itemFilter = FilterItemRegistry.LoadItemFilter(AttachItem.type, _filterConfig ?? new TagCompound());
		return _itemFilter;
	}

	// UiItemFilterHandler is NOT overridden - upstream's ItemFilterCover has
	// no install-slot.
	public override SimpleItemFilter? UiItemFilter => GetItemFilter() as SimpleItemFilter;
	public override TagItemFilter?    UiTagItemFilter => GetItemFilter() as TagItemFilter;

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

	// Reset lazy filter so the new AttachItem dispatches via FilterItemRegistry
	// on next read (pipe-UI swap + Save/Load roundtrips).
	public override void OnAttached(Item itemStack)
	{
		base.OnAttached(itemStack);
		_itemFilter = null;
	}

	public override bool CanAttach() => base.CanAttach() && CoverHolder is IItemHandler;

	public override IItemHandler? GetItemHandlerCap(IItemHandler? defaultValue)
	{
		if (defaultValue == null) return null;
		if (_itemFilterWrapper == null || _itemFilterWrapper.Inner != defaultValue)
			_itemFilterWrapper = new FilteredItemHandlerWrapper(this, defaultValue);
		return _itemFilterWrapper;
	}

	private sealed class FilteredItemHandlerWrapper : ItemHandlerDelegate
	{
		private readonly ItemFilterCover _cover;

		public FilteredItemHandlerWrapper(ItemFilterCover cover, IItemHandler inner) : base(inner) => _cover = cover;

		public override Item Insert(int slot, Item stack, bool simulate)
		{
			if (_cover._filterMode == FilterMode.FilterExtract)
			{
				if (_cover._allowFlow == ManualIOMode.Disabled) return stack;
				if (_cover._allowFlow == ManualIOMode.Unfiltered) return base.Insert(slot, stack, simulate);
			}
			if (!_cover.GetItemFilter().Test(stack)) return stack;
			return base.Insert(slot, stack, simulate);
		}

		public override Item Extract(int slot, int amount, bool simulate)
		{
			if (_cover._filterMode == FilterMode.FilterInsert)
			{
				if (_cover._allowFlow == ManualIOMode.Disabled) return new Item();
				if (_cover._allowFlow == ManualIOMode.Unfiltered) return base.Extract(slot, amount, simulate);
			}
			Item result = base.Extract(slot, amount, true);
			if (result.IsAir || !_cover.GetItemFilter().Test(result)) return new Item();
			return simulate ? result : base.Extract(slot, amount, false);
		}
	}

	public override void Save(TagCompound tag)
	{
		base.Save(tag);
		tag["filterMode"] = (int)_filterMode;
		tag["manualIO"] = (int)_allowFlow;
		// Refresh _filterConfig from the live filter so player edits persist.
		if (_itemFilter is IFilter<Item> f)
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
		_itemFilter = null;   // force lazy reload from AttachItem + _filterConfig
	}
}
