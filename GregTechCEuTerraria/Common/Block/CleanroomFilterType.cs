#nullable enable
using GregTechCEuTerraria.Api.Machine.Multiblock;

namespace GregTechCEuTerraria.Common.Block;

// Port of com.gregtechceu.gtceu.common.block.CleanroomFilterType.
//
// Maps a cleanroom filter casing tile to the CleanroomType it provides. The
// multiblock matcher reads this through `CleanroomFilters` predicate and the
// controller's `OnStructureFormed` picks up the resolved filter via match
// context to set its `cleanroomType`.
//
// Documented adaptations:
//   - Java enum -> static readonly instances (we don't ship `IFilterType` as a
//     hierarchy because there's no other filter family yet).
//   - `getSerializedName()` is the tile name (matches upstream's id stripped
//     of namespace).
public sealed class CleanroomFilterType
{
	public static readonly CleanroomFilterType FILTER_CASING =
		new("filter_casing", CleanroomType.CLEANROOM);
	public static readonly CleanroomFilterType FILTER_CASING_STERILE =
		new("sterilizing_filter_casing", CleanroomType.STERILE_CLEANROOM);

	public string TileName { get; }
	public CleanroomType CleanroomType { get; }

	private CleanroomFilterType(string tileName, CleanroomType cleanroomType)
	{
		TileName = tileName;
		CleanroomType = cleanroomType;
	}

	public static CleanroomFilterType[] All => new[] { FILTER_CASING, FILTER_CASING_STERILE };

	public override string ToString() => TileName;
}
