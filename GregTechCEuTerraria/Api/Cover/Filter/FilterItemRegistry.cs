#nullable enable
using System;
using System.Collections.Generic;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Cover.Filter;

// Adaptation of upstream's ItemFilter.FILTERS / FluidFilter.FILTERS maps -
// keyed by the filter ITEM. Upstream keys by `ItemLike`; we key by the
// Terraria item type id and carry the config TagCompound (since a Terraria
// Item has no NBT bag - see IFilter). A TerrariaCompat loader populates this
// at mod load once the filter items have their ItemIDs.
public static class FilterItemRegistry
{
	private static readonly Dictionary<int, Func<TagCompound, IItemFilter>> _itemFilters = new();
	private static readonly Dictionary<int, Func<TagCompound, IFluidFilter>> _fluidFilters = new();

	public static void RegisterItemFilter(int itemType, Func<TagCompound, IItemFilter> loader) =>
		_itemFilters[itemType] = loader;

	public static void RegisterFluidFilter(int itemType, Func<TagCompound, IFluidFilter> loader) =>
		_fluidFilters[itemType] = loader;

	public static bool IsItemFilter(int itemType) => _itemFilters.ContainsKey(itemType);
	public static bool IsFluidFilter(int itemType) => _fluidFilters.ContainsKey(itemType);

	public static IItemFilter LoadItemFilter(int itemType, TagCompound config) =>
		_itemFilters.TryGetValue(itemType, out var loader) ? loader(config) : IItemFilter.Empty;

	public static IFluidFilter LoadFluidFilter(int itemType, TagCompound config) =>
		_fluidFilters.TryGetValue(itemType, out var loader) ? loader(config) : IFluidFilter.Empty;

	public static void Clear()
	{
		_itemFilters.Clear();
		_fluidFilters.Clear();
	}
}
