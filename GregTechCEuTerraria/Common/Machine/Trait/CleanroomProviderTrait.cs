#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Machine.Multiblock;
using GregTechCEuTerraria.Api.Machine.Trait;

namespace GregTechCEuTerraria.Common.Machine.Trait;

// Port of com.gregtechceu.gtceu.common.machine.trait.CleanroomProviderTrait.
//
// Attached to anything that supplies a Cleanroom capability - the Cleanroom
// multiblock controller, the Cleaning Maintenance Hatch. Holds:
//   - `ProvidedTypes`: the set of CleanroomType values this provider satisfies
//     (e.g. just `CLEANROOM`, or `CLEANROOM + STERILE_CLEANROOM` for a sterile
//     provider - a sterile provider also satisfies plain cleanroom recipes).
//   - `IsActive`: whether the provider is currently producing its environment
//     (the multi is structure-formed + powered; the hatch is bound to a multi).
//
// `CleanroomReceiverTrait` consults both fields via `HasActiveCleanroom`.
//
// Documented adaptations: Lombok @Getter/@Setter inlined; ObjectOpenHashSet
// replaced with HashSet (we don't need fastutil's micro-optimizations here).
public sealed class CleanroomProviderTrait : MachineTrait
{
	public static readonly MachineTraitType<CleanroomProviderTrait> TYPE =
		new(allowMultipleInstances: false);

	public override MachineTraitType TraitType => TYPE;

	public HashSet<CleanroomType> ProvidedTypes { get; set; }
	public bool IsActive { get; set; }

	public CleanroomProviderTrait()
		: this(new HashSet<CleanroomType> { CleanroomType.CLEANROOM }) { }

	public CleanroomProviderTrait(IEnumerable<CleanroomType> providedTypes)
	{
		ProvidedTypes = new HashSet<CleanroomType>(providedTypes);
		IsActive = false;
	}
}
