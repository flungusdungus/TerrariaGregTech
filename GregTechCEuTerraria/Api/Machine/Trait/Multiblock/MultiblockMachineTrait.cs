#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

namespace GregTechCEuTerraria.Api.Machine.Trait.Multiblock;

// Port of com.gregtechceu.gtceu.api.machine.trait.multiblock.
// MultiblockMachineTrait.
//
// Base for machine traits that can ONLY be attached to multiblock controllers.
// Adds the structure-formed / structure-invalid lifecycle hooks that the
// controller's `OnStructureFormed` / `OnStructureInvalid` fan out to.
//
// Documented adaptations:
//   - `getMachine()` covariant override -> C# `Machine` is non-virtual; we
//     expose a separate typed accessor `Controller` instead.
//   - `validMachineClasses` -> C# `ValidMachineClasses` (already abstract on
//     MachineTrait).
public abstract class MultiblockMachineTrait : MachineTrait
{
	protected MultiblockMachineTrait() : base() { }

	// Typed alias for the back-reference. Equivalent to upstream's covariant
	// `getMachine()` override; in C# we can't override the property type, so
	// callers use this helper instead.
	public MultiblockControllerMachine Controller => (MultiblockControllerMachine)Machine;

	protected override IReadOnlyList<Type> ValidMachineClasses() =>
		new[] { typeof(MultiblockControllerMachine) };

	// Called when the multiblock structure forms (the controller's
	// `OnStructureFormed` fans out to every attached MultiblockMachineTrait).
	public virtual void OnStructureFormed() { }

	// Called when the multiblock structure becomes invalid (the controller's
	// `OnStructureInvalid` fans out to every attached MultiblockMachineTrait).
	public virtual void OnStructureInvalid() { }
}
