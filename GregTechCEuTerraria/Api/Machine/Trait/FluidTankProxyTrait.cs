#nullable enable
using System;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.Api.Machine.Trait;

// Port of com.gregtechceu.gtceu.api.machine.trait.FluidTankProxyTrait.
//
// A trait that exposes itself as an `IFluidHandler` but delegates every
// operation to a settable `Proxy` reference - the actual tank lives
// somewhere else (typically the multiblock controller's storage). Used by
// `TankValvePartMachine` to surface the controller's tank as a single-face
// fluid I/O point.
//
// Documented adaptations (forced by codebase architecture, not chosen):
//   - `Direction... facings` -> `IODirection[] facings` (2D port: 4 cardinals,
//     not 6 faces).
//   - `GTTransferUtils.transferFluidsFiltered` -> `AdjacentFluidPush.Push`
//     (we factored push helpers out into standalone statics; behavior is
//     equivalent - same per-side cover filter via `Machine.
//     GetFluidCapFilter(facing, IO.OUT)`).
//   - `FluidAction.EXECUTE / SIMULATE` -> `bool simulate` (codebase-wide).
public sealed class FluidTankProxyTrait : MachineTrait, IFluidHandlerModifiable, ICapabilityTrait
{
	public static readonly MachineTraitType<FluidTankProxyTrait> TYPE = new(allowMultipleInstances: true);
	public override MachineTraitType TraitType => TYPE;

	public IO                       CapabilityIO { get; }
	public IFluidHandlerModifiable? Proxy { get; set; }

	public FluidTankProxyTrait(IO capabilityIO) : base()
	{
		CapabilityIO = capabilityIO;
	}

	// === ICapabilityTrait ====================================================
	// Named methods restored on the trait (C# DIMs aren't visible via
	// `this.X()`, so we provide instance methods that mirror upstream's
	// Lombok-equivalent Java-default surface).
	public IO   GetCapabilityIO() => CapabilityIO;
	public bool CanCapInput()     => CapabilityIO.Supports(IO.IN);
	public bool CanCapOutput()    => CapabilityIO.Supports(IO.OUT);

	// === IFluidHandler - direct delegation to Proxy with capability gating ==

	public int TankCount => Proxy?.TankCount ?? 0;

	public FluidStack GetTank(int tank) =>
		Proxy?.GetTank(tank) ?? FluidStack.Empty;

	public int GetCapacity(int tank) =>
		Proxy?.GetCapacity(tank) ?? 0;

	public bool IsFluidValid(int tank, FluidStack fluid) =>
		Proxy != null && Proxy.IsFluidValid(tank, fluid);

	// Modifiable surface - direct passthrough (no IO gating, mirrors
	// upstream IFluidHandlerModifiable parity).
	public void SetFluidInTank(int tank, FluidStack stack)
	{
		if (Proxy != null) Proxy.SetFluidInTank(tank, stack);
	}

	public int Fill(FluidStack fluid, bool simulate)
	{
		if (Proxy != null && CanCapInput()) return Proxy.Fill(fluid, simulate);
		return 0;
	}

	public FluidStack Drain(int maxAmount, bool simulate)
	{
		if (Proxy != null && CanCapOutput()) return Proxy.Drain(maxAmount, simulate);
		return FluidStack.Empty;
	}

	public FluidStack Drain(FluidStack fluidStack, bool simulate)
	{
		if (Proxy != null && CanCapOutput()) return Proxy.Drain(fluidStack, simulate);
		return FluidStack.Empty;
	}

	public IFluidHandler GetTankAccess(int tank) =>
		Proxy?.GetTankAccess(tank) ?? this;

	// === Internal (capability-gate-bypassing) variants - upstream parity ====

	public int FillInternal(FluidStack resource, bool simulate)
	{
		if (Proxy == null || resource.IsEmpty) return 0;
		return Proxy.Fill(resource, simulate);
	}

	public FluidStack DrainInternal(FluidStack resource, bool simulate)
	{
		if (Proxy == null || resource.IsEmpty) return FluidStack.Empty;
		return Proxy.Drain(resource, simulate);
	}

	public FluidStack DrainInternal(int maxDrain, bool simulate)
	{
		if (Proxy == null) return FluidStack.Empty;
		return Proxy.Drain(maxDrain, simulate);
	}

	// === Emptiness check (upstream fast-path for NotifiableFluidTank) =======

	public bool IsEmpty()
	{
		if (Proxy is NotifiableFluidTank nft) return nft.IsEmpty();
		if (Proxy == null) return true;
		for (int i = 0; i < Proxy.TankCount; i++)
			if (!Proxy.GetTank(i).IsEmpty) return false;
		return true;
	}

	// === Auto-output helper (upstream parity) ===============================
	//
	// Push the proxy's contents to adjacent fluid handlers on `facings`. Per-
	// side cover filter applied via `Machine.GetFluidCapFilter(facing, IO.OUT)`
	// - `AdjacentFluidPush.Push` reads this internally.
	public void ExportToNearby(params IODirection[] facings)
	{
		if (IsEmpty() || Proxy == null) return;
		if (Machine is not MetaMachine mm) return;
		// Push from THIS proxy specifically (not via the machine's
		// IFluidHandler face) - see the explicit-handler overload's
		// header. Matters when the part has multiple proxy traits and
		// only one is the auto-output source.
		foreach (var facing in facings)
			AdjacentFluidPush.Push(mm, this, 0, Proxy.TankCount, maxAmount: int.MaxValue, side: facing);
	}
}
