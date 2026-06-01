#nullable enable
using System;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Cover.Filter;

// Port of com.gregtechceu.gtceu.api.cover.filter.FilterHandler<T, F>.
//
// Owns the filter item installed on a cover + the live filter instance built
// from it. A cover (conveyor / pump / voiding) holds one of these and routes
// Test() through it.
//
// Documented adaptations:
//   - ISyncManaged / SyncDataHolder dropped - the handler's state (filter item
//     + filter config) is persisted into the owning cover's save blob via
//     Save / Load, and rides the machine sync packet like every other cover
//     field.
//   - createFilterSlotUI / createFilterConfigUI (LDLib widgets) dropped - cover
//     settings UI is a later phase.
//   - The filter's config lived in the filter ItemStack's NBT upstream. A
//     Terraria Item has no NBT bag, so the handler carries the config
//     TagCompound itself (`_filterConfig`) and persists it alongside the item.
//   - The onFilterLoaded/Removed/Updated callbacks take no argument (every
//     upstream call site ignores the filter argument - see IFilter).
public abstract class FilterHandler<TResource, TFilter> where TFilter : class, IFilter<TResource>
{
	public Item FilterItem { get; private set; } = new();

	private TFilter? _filter;
	private TagCompound _filterConfig = new();

	public Action OnFilterLoaded { get; private set; } = () => { };
	public Action OnFilterRemoved { get; private set; } = () => { };
	public Action OnFilterUpdated { get; private set; } = () => { };

	// Builds the concrete filter from the installed item + its persisted config.
	protected abstract TFilter LoadFilterFrom(Item filterItem, TagCompound config);

	// The all-pass filter returned when no filter item is installed.
	protected abstract TFilter EmptyFilter { get; }

	// Whether the given item is a valid filter for this handler.
	public abstract bool CanInsertFilterItem(Item itemStack);

	public FilterHandler<TResource, TFilter> WithFilterLoaded(Action onFilterLoaded)
	{
		OnFilterLoaded = onFilterLoaded;
		return this;
	}

	public FilterHandler<TResource, TFilter> WithFilterRemoved(Action onFilterRemoved)
	{
		OnFilterRemoved = onFilterRemoved;
		return this;
	}

	public FilterHandler<TResource, TFilter> WithFilterUpdated(Action onFilterUpdated)
	{
		OnFilterUpdated = onFilterUpdated;
		return this;
	}

	public bool IsFilterPresent => _filter != null || !FilterItem.IsAir;

	public TFilter GetFilter()
	{
		if (_filter == null)
		{
			if (FilterItem.IsAir) return EmptyFilter;
			LoadFilterFromItem();
		}
		return _filter ?? EmptyFilter;
	}

	public bool Test(TResource resource) => GetFilter().Test(resource);

	public void SetFilterItem(Item item)
	{
		FilterItem = item ?? new Item();
		_filterConfig = new TagCompound();
		UpdateFilter();
	}

	private void UpdateFilter()
	{
		if (_filter != null)
		{
			_filter = null;
			OnFilterRemoved();
		}
		LoadFilterFromItem();
	}

	private void LoadFilterFromItem()
	{
		if (FilterItem.IsAir || !CanInsertFilterItem(FilterItem)) return;

		_filter = LoadFilterFrom(FilterItem, _filterConfig);
		_filter.OnUpdated = () =>
		{
			_filterConfig = _filter.SaveFilter() ?? new TagCompound();
			OnFilterUpdated();
		};
		OnFilterLoaded();
	}

	// === Persistence (into the owning cover's blob) =========================

	public void Save(TagCompound tag)
	{
		tag["filterItem"] = ItemIO.Save(FilterItem);
		if (_filter != null) _filterConfig = _filter.SaveFilter() ?? new TagCompound();
		tag["filterConfig"] = _filterConfig;
	}

	public void Load(TagCompound tag)
	{
		if (tag.ContainsKey("filterItem"))
			FilterItem = ItemIO.Load(tag.GetCompound("filterItem"));
		_filterConfig = tag.ContainsKey("filterConfig")
			? tag.GetCompound("filterConfig")
			: new TagCompound();
		_filter = null;
		LoadFilterFromItem();
	}
}

// Concrete item-filter handler - upstream's FilterHandlers.item(container)
// anonymous subclass.
public sealed class ItemFilterHandler : FilterHandler<Item, IItemFilter>
{
	// `container` is the holder (cover OR multiblock part - upstream
	// `FilterHandlers.item(this)` is called from both). The arg is discarded;
	// kept for upstream-call-site parity.
	public ItemFilterHandler(object container) { _ = container; }

	protected override IItemFilter EmptyFilter => IItemFilter.Empty;

	protected override IItemFilter LoadFilterFrom(Item filterItem, TagCompound config) =>
		FilterItemRegistry.LoadItemFilter(filterItem.type, config);

	public override bool CanInsertFilterItem(Item itemStack) =>
		!itemStack.IsAir && FilterItemRegistry.IsItemFilter(itemStack.type);
}

// Concrete fluid-filter handler - upstream's FilterHandlers.fluid(container).
public sealed class FluidFilterHandler : FilterHandler<Api.Fluids.FluidStack, IFluidFilter>
{
	public FluidFilterHandler(object container) { _ = container; }

	protected override IFluidFilter EmptyFilter => IFluidFilter.Empty;

	protected override IFluidFilter LoadFilterFrom(Item filterItem, TagCompound config) =>
		FilterItemRegistry.LoadFluidFilter(filterItem.type, config);

	public override bool CanInsertFilterItem(Item itemStack) =>
		!itemStack.IsAir && FilterItemRegistry.IsFluidFilter(itemStack.type);
}

// Port of com.gregtechceu.gtceu.api.cover.filter.FilterHandlers - the
// item() / fluid() factory.
public static class FilterHandlers
{
	public static ItemFilterHandler  Item (object container) => new(container);
	public static FluidFilterHandler Fluid(object container) => new(container);
}
