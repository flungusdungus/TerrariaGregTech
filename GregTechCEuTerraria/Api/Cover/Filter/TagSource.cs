#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Fluids;
using Terraria;

namespace GregTechCEuTerraria.Api.Cover.Filter;

// The set of tag ids a given item / fluid carries.
//
// Upstream reads this straight off the Minecraft tag system (itemStack.getTags()
// / fluid.defaultFluidState().getTags()). Terraria has no tag system, so
// TerrariaCompat populates these delegates at mod load - wiring them to a
// reverse index built from the datagen tag dump plus the synthesized
// material-prefix tags. TagItemFilter / TagFluidFilter query through here so the
// Api layer stays free of mod-side registry dependencies (same pattern as
// FilterItemRegistry).
public static class TagSource
{
	private static readonly IReadOnlyCollection<string> Empty = Array.Empty<string>();

	public static Func<Item, IReadOnlyCollection<string>>? ItemTags { get; set; }
	public static Func<FluidStack, IReadOnlyCollection<string>>? FluidTags { get; set; }

	public static IReadOnlyCollection<string> TagsOf(Item item) =>
		ItemTags?.Invoke(item) ?? Empty;

	public static IReadOnlyCollection<string> TagsOf(FluidStack fluid) =>
		FluidTags?.Invoke(fluid) ?? Empty;
}
