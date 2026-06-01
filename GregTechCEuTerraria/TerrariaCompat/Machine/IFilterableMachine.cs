#nullable enable
using GregTechCEuTerraria.Api.Cover.Filter;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Per-machine SimpleItemFilter + TagItemFilter pair (ordinal: 0 items, 1 tag),
// edited server-authoritatively through MachineFilterAction. Same shape as
// MagnetItem (client-side, no action packet). Matcher click math shared via
// Api.Cover.Filter.ItemFilterEdit.
public interface IFilterableMachine
{
	int FilterOrdinal { get; set; }
	SimpleItemFilter SimpleFilter { get; }
	TagItemFilter    TagFilter    { get; }

	IItemFilter ActiveFilter() => FilterOrdinal == 1 ? TagFilter : SimpleFilter;
}
