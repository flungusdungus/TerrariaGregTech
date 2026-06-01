#nullable enable
namespace GregTechCEuTerraria.Api.Cover;

// Port of com.gregtechceu.gtceu.api.cover.CoverDefinition.
//
// Identity + factory for a kind of cover. CreateCoverBehavior builds a fresh
// CoverBehavior bound to a specific holder + side.
//
// Documented adaptations:
//   - The ICoverRenderer supplier is dropped - covers are UI-only in our port
//     (Terraria is 2D; a cover is configured in the machine GUI, never drawn
//     on the machine).
//   - TieredCoverBehaviourProvider is dropped - tiered covers (e.g. solar
//     panels) register one CoverDefinition per tier with the tier captured in
//     the provider closure, exactly as upstream's registerTiered does.
public sealed class CoverDefinition
{
	public delegate CoverBehavior CoverBehaviourProvider(
		CoverDefinition definition, ICoverable coverable, CoverSide side);

	public string Id { get; }
	private readonly CoverBehaviourProvider _behaviorCreator;

	public CoverDefinition(string id, CoverBehaviourProvider behaviorCreator)
	{
		Id = id;
		_behaviorCreator = behaviorCreator;
	}

	public CoverBehavior CreateCoverBehavior(ICoverable coverable, CoverSide side) =>
		_behaviorCreator(this, coverable, side);
}
