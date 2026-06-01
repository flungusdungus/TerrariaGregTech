#nullable enable
using System;

namespace GregTechCEuTerraria.Api.Machine.Trait;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.machine.trait.MachineTraitType.
// DO NOT modify behavior; mirror upstream changes only.
//
// Identity token for a trait class. One static readonly instance per concrete
// trait type, e.g.:
//
//   public sealed class NotifiableEnergyContainer
//     : NotifiableRecipeHandlerTrait<EnergyStack> {
//       public static readonly MachineTraitType<NotifiableEnergyContainer>
//         TYPE = new(allowMultipleInstances: true);
//       public override MachineTraitType TraitType => TYPE;
//       ...
//   }
//
// allowMultipleInstances:
//   - true  -> machine can attach multiple traits of this type (multiple
//             NotifiableFluidTanks, multiple item handlers).
//   - false -> exactly one allowed (RecipeLogic, MachineCoverContainer).
//
// Two-level shape: the abstract `MachineTraitType` base is the
// non-generic key used by holder dispatch + `MachineTrait.TraitType`
// (mirroring upstream's `MachineTraitType<?>` wildcard return). The sealed
// generic `MachineTraitType<T>` is the typed token declared per-trait;
// lookups via `holder.GetTrait(token)` infer T automatically.
public abstract class MachineTraitType
{
	public Type Clazz { get; }
	public bool AllowMultipleInstances { get; }

	protected MachineTraitType(Type clazz, bool allowMultipleInstances)
	{
		Clazz = clazz;
		AllowMultipleInstances = allowMultipleInstances;
	}

	// Mirrors upstream's `allowsMultipleInstances()` method-style accessor.
	public bool AllowsMultipleInstances() => AllowMultipleInstances;
}

// Typed token. Mirrors upstream `MachineTraitType<T extends MachineTrait>`.
// Declared per-trait as a static readonly field on the trait class.
public sealed class MachineTraitType<T> : MachineTraitType where T : MachineTrait
{
	// Verbatim port of upstream's `MachineTraitType(Class<T> clazz)` -
	// defaults allowMultipleInstances to true.
	public MachineTraitType() : base(typeof(T), allowMultipleInstances: true) { }

	// Verbatim port of upstream's
	// `MachineTraitType(Class<T> clazz, boolean allowMultipleInstances)`.
	public MachineTraitType(bool allowMultipleInstances)
		: base(typeof(T), allowMultipleInstances) { }

	// Verbatim port of upstream's `T castTrait(MachineTrait trait)`. C#'s
	// direct cast does the same runtime check Java's `clazz.cast(...)` does.
	public T CastTrait(MachineTrait trait) => (T)trait;
}
