#nullable enable
using System;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Cover.Filter;

// Port of com.gregtechceu.gtceu.api.cover.filter.ItemFilter.
//
// The FILTERS registry (item -> filter loader) is collapsed into
// ItemFilterHandler.LoadFilter, which dispatches on the filter item's type.
public interface IItemFilter : IFilter<Item>
{
	// Configured count for the supplied item; 0 if the item is not matched.
	int TestItemCount(Item itemStack);

	// Whether this filter supports exact-amount queries.
	bool SupportsAmounts => !IsBlackList;

	// An empty item filter that allows all items. ONLY for matching - all other
	// members are inert (upstream throws NotImplementedException here; we return
	// harmless no-ops since there is no UI to surface the failure).
	static readonly IItemFilter Empty = new EmptyItemFilter();

	private sealed class EmptyItemFilter : IItemFilter
	{
		public int TestItemCount(Item itemStack) => int.MaxValue;
		public bool Test(Item itemStack) => true;
		public TagCompound? SaveFilter() => null;
		public Action OnUpdated { get; set; } = () => { };
	}
}
