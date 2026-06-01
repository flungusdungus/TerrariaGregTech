#nullable enable
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Cover.Filter;

// Port of com.gregtechceu.gtceu.api.cover.filter.TagItemFilter.
//
// Matches an item against the tag expression. `supportsAmounts` is false - a
// tag match is all-or-nothing, so TestItemCount returns MAX or 0.
public sealed class TagItemFilter : TagFilter, IItemFilter
{
	// Per-item-type result cache (upstream caches by Item, ignoring count/NBT).
	private readonly Dictionary<int, bool> _cache = new();

	public bool SupportsAmounts => false;

	public static TagItemFilter LoadFilter(TagCompound tag)
	{
		var filter = new TagItemFilter();
		filter.LoadOreDict(tag);
		return filter;
	}

	public override void SetOreDict(string oreDict)
	{
		_cache.Clear();
		base.SetOreDict(oreDict);
	}

	public bool Test(Item itemStack)
	{
		if (string.IsNullOrEmpty(OreDictFilterExpression)) return false;
		if (itemStack.IsAir) return false;
		if (_cache.TryGetValue(itemStack.type, out bool cached)) return cached;

		bool result = TagExprFilter.TagsMatch(MatchExpr, TagSource.TagsOf(itemStack));
		_cache[itemStack.type] = result;
		return result;
	}

	public int TestItemCount(Item itemStack) => Test(itemStack) ? int.MaxValue : 0;
}
