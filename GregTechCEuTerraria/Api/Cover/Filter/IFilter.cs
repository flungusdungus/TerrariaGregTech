#nullable enable
using System;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Cover.Filter;

// Port of com.gregtechceu.gtceu.api.cover.filter.Filter<T, S>.
//
// A filter matches resources of type T (items / fluids). Installed into a
// FilterHandler on a conveyor / pump / filter cover.
//
// Documented adaptations:
//   - The Predicate<T> supertype -> an explicit Test(T) method.
//   - openConfigurator(x, y) (returns an LDLib WidgetGroup) is dropped - cover
//     settings screens are a later UI phase (see IUICover). Matching, amount
//     queries and persistence are ported in full; a filter with no UI yet just
//     starts blank (matches everything).
//   - The recursive S self-type parameter is dropped. Its only consumers were
//     the UI configurator and the onUpdated callback's argument - and every
//     call site ignores that argument (`f -> configureFilter()`). OnUpdated is
//     a parameterless Action.
//   - saveFilter()/loadFilter wrote into the host ItemStack's NBT. A Terraria
//     Item has no generic NBT bag, so a filter's config is persisted into the
//     owning cover's save blob instead (FilterHandler.Save -> cover Save).
public interface IFilter<T>
{
	// Whether the resource passes the filter.
	bool Test(T resource);

	// Serialised filter config, or null when the filter is blank.
	TagCompound? SaveFilter();

	// Fired whenever the filter's config changes - the FilterHandler hooks this
	// to re-persist and to notify the cover.
	Action OnUpdated { get; set; }

	// Inverts matching - a blacklist passes everything NOT configured.
	bool IsBlackList => false;

	// True when the filter has no configuration at all (default state).
	bool IsBlank => false;
}
