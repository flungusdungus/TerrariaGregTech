#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.ItemPipe;

// Verbatim port of upstream ItemPipeNet.ITEMNETSET selector. Restricted /
// NonRestricted split is what ROUND_ROBIN_PRIO uses (fallback bucket).
public enum ItemRoutePathSet : byte
{
	Full          = 0,
	NonRestricted = 1,
	Restricted    = 2,
}
