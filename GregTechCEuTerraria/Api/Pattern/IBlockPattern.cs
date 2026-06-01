#nullable enable
namespace GregTechCEuTerraria.Api.Pattern;

// The matcher-side contract for "a multiblock pattern" - fixed or
// repeatable. Both `BlockPattern` (fixed shape) and `RepeatableBlockPattern`
// (variable-size - top/bodyxN/tail) implement it; `MultiblockController
// Machine.GetPattern()` returns this so consumers don't care which form
// underlies a given controller.
public interface IBlockPattern
{
	// Test the world against this pattern, anchored at the controller's
	// tile (read from `state.ControllerPosX/Y`). Returns true on full match;
	// on failure `state.Error` carries the first failure reason. `save
	// Predicate` records a `tilePos -> predicate` map in the match context
	// (for post-match per-cell role lookup).
	bool CheckPatternAt(MultiblockState state, bool savePredicate = false);

	// Materialised shape used by the in-world preview renderer when the
	// controller isn't formed yet. For `BlockPattern` this is the pattern
	// itself; for `RepeatableBlockPattern` it's the MaxRepeats build (the
	// largest the structure could grow to), so the player sees the full
	// outline they have room for.
	BlockPattern GetPreviewPattern();
}
