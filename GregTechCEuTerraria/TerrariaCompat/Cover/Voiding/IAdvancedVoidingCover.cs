#nullable enable
using GregTechCEuTerraria.Api.Cover.Data;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Voiding;

// UI-read surface shared by the advanced voiding covers (item + fluid) so the
// cover settings popup can read their mode / limit without per-type casts.
// AdvancedItemVoidingCover and AdvancedFluidVoidingCover live in separate class
// hierarchies (ItemVoidingCover / FluidVoidingCover), so a small shared
// interface is the clean join point. Mutation still goes through the
// server-authoritative CoverBehavior.ApplySetting.
public interface IAdvancedVoidingCover
{
	VoidingMode VoidingMode { get; }

	// Per-type amount kept in VoidOverflow mode (items: count, fluids: mB).
	int VoidLimit { get; }
}
