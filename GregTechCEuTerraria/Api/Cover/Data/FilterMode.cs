#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;

namespace GregTechCEuTerraria.Api.Cover.Data;

// Port of com.gregtechceu.gtceu.common.cover.data.FilterMode.
public enum FilterMode
{
	FilterInsert,
	FilterExtract,
	FilterBoth,
}

public static class FilterModeExtensions
{
	// Port of FilterMode.filters(IO) - whether this mode filters transfer in
	// the given IO direction.
	public static bool Filters(this FilterMode mode, IO io) => mode switch
	{
		FilterMode.FilterBoth    => true,
		FilterMode.FilterInsert  => io == IO.IN,
		FilterMode.FilterExtract => io == IO.OUT,
		_                        => false,
	};
}
