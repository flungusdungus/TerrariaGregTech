#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Fluids;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Cover.Filter;

// Port of com.gregtechceu.gtceu.api.cover.filter.TagFluidFilter.
//
// Fluid mirror of TagItemFilter. NB: upstream's datagen ships only ~3 fluid
// tags, so this filter is faithful but thin on content - that is upstream's
// data, not a porting gap.
public sealed class TagFluidFilter : TagFilter, IFluidFilter
{
	// Per-fluid-type result cache (keyed by FluidType id).
	private readonly Dictionary<string, bool> _cache = new();

	public bool SupportsAmounts => false;

	public static TagFluidFilter LoadFilter(TagCompound tag)
	{
		var filter = new TagFluidFilter();
		filter.LoadOreDict(tag);
		return filter;
	}

	public override void SetOreDict(string oreDict)
	{
		_cache.Clear();
		base.SetOreDict(oreDict);
	}

	public bool Test(FluidStack fluidStack)
	{
		if (string.IsNullOrEmpty(OreDictFilterExpression)) return false;
		if (fluidStack.Type is null) return false;
		if (_cache.TryGetValue(fluidStack.Type.Id, out bool cached)) return cached;

		bool result = TagExprFilter.TagsMatch(MatchExpr, TagSource.TagsOf(fluidStack));
		_cache[fluidStack.Type.Id] = result;
		return result;
	}

	public int TestFluidAmount(FluidStack fluidStack) => Test(fluidStack) ? int.MaxValue : 0;
}
