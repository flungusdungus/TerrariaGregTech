#nullable enable
using GregTechCEuTerraria.Api.Machine.Multiblock;
using GregTechCEuTerraria.Api.Machine.Trait;

namespace GregTechCEuTerraria.Common.Machine.Trait;

// Port of com.gregtechceu.gtceu.common.machine.trait.CleanroomReceiverTrait.
//
// Attached to any machine that may need a Cleanroom - the recipe-running side
// of the producer/consumer pair. A `CleaningMaintenanceHatchPartMachine` or
// (future) `CleanroomMachine` binds its `CleanroomProviderTrait` here via
// `SetCleanroomProvider`; `CleanroomCondition.Test` then asks the receiver
// `HasActiveCleanroom(type)`.
//
// The bound provider is nullable - no cleanroom -> no provider -> predicate
// returns false -> cleanroom-gated recipes refuse to match.
public sealed class CleanroomReceiverTrait : MachineTrait
{
	public static readonly MachineTraitType<CleanroomReceiverTrait> TYPE =
		new(allowMultipleInstances: false);

	public override MachineTraitType TraitType => TYPE;

	public CleanroomProviderTrait? CleanroomProvider { get; set; }

	public CleanroomReceiverTrait() { CleanroomProvider = null; }

	public bool HasActiveCleanroom(CleanroomType type) =>
		CleanroomProvider != null && CleanroomProvider.IsActive
		&& CleanroomProvider.ProvidedTypes.Contains(type);

	public void RemoveCleanroom() { CleanroomProvider = null; }
}
