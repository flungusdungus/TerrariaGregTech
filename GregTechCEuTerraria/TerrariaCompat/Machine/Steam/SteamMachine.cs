#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine.Trait;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Steam;

// 1:1 port of SteamMachine. Extends MetaMachine directly (no EU).
// IsHighPressure = upstream's ITieredMachine flag (LP=0/HP=1).
public abstract class SteamMachine : MetaMachine, IFluidHandler
{
	public bool IsHighPressure => Definition?.IsHighPressure ?? false;

	private NotifiableFluidTank? _steamTank;
	public NotifiableFluidTank SteamTank
	{
		get { EnsureSteamTraits(); return _steamTank!; }
	}

	public int SteamTier => IsHighPressure ? 1 : 0;

	protected SteamMachine() : base() { }
	// Legacy ctor preserved for boiler subclasses pending deletion.
	protected SteamMachine(bool isHighPressure) : base() { }

	// Upstream 16 * FluidType.BUCKET_VOLUME.
	protected virtual int SteamTankCapacity => 16_000;

	protected virtual void EnsureSteamTraits()
	{
		if (_steamTank is not null) return;
		BindDefinition();

		_steamTank = CreateSteamTank();
		_steamTank.SetFilter(fluid => !fluid.IsEmpty && fluid.Type!.Id == FluidRegistry.Steam.Id);
		Traits.Attach(_steamTank);
		Traits.RegisterPersistent("SteamTank", _steamTank);
	}

	// Boiler subclass overrides for the IO.IN water tank.
	protected virtual NotifiableFluidTank CreateSteamTank() =>
		new(1, SteamTankCapacity, Api.Capability.Recipe.IO.OUT);

	public virtual int TankCount { get { EnsureSteamTraits(); return _steamTank!.GetTanks(); } }

	public virtual FluidStack GetTank(int tank) { EnsureSteamTraits(); return _steamTank!.GetFluidInTank(tank); }

	public virtual int GetCapacity(int tank) => SteamTankCapacity;

	// IO-direction-keyed so producer (boiler, OUT) and consumer (IN) subclasses
	// both work without per-class overrides.
	public virtual bool IsFluidValid(int tank, FluidStack fluid)
	{
		EnsureSteamTraits();
		if (_steamTank!.HandlerIO != Api.Capability.Recipe.IO.IN) return false;
		return !fluid.IsEmpty && fluid.Type!.Id == FluidRegistry.Steam.Id;
	}

	public virtual int Fill(FluidStack fluid, bool simulate)
	{
		EnsureSteamTraits();
		if (_steamTank!.HandlerIO != Api.Capability.Recipe.IO.IN) return 0;
		return _steamTank.Fill(fluid, simulate);
	}

	public virtual FluidStack Drain(int maxAmount, bool simulate)
	{
		if (maxAmount <= 0) return FluidStack.Empty;
		EnsureSteamTraits();
		return _steamTank!.Drain(maxAmount, simulate);
	}

	public virtual FluidStack Drain(FluidStack fluid, bool simulate)
	{
		if (fluid.IsEmpty) return FluidStack.Empty;
		EnsureSteamTraits();
		return _steamTank!.Drain(fluid, simulate);
	}

	// SteamBoilerMachine overrides to route water vs steam by index.
	public virtual IFluidHandler GetTankAccess(int tank)
	{
		EnsureSteamTraits();
		return _steamTank!.Storages[0];
	}

	// Producer (OUT) = drain-only; consumer (IN) = fill-only.
	public virtual (bool AllowFill, bool AllowDrain) GetTankClickCaps(int tank)
	{
		EnsureSteamTraits();
		return _steamTank!.HandlerIO == Api.Capability.Recipe.IO.IN
			? (true,  false)
			: (false, true);
	}

	public override void SaveData(Terraria.ModLoader.IO.TagCompound tag)
	{
		EnsureSteamTraits();
		base.SaveData(tag);
	}

	public override void LoadData(Terraria.ModLoader.IO.TagCompound tag)
	{
		EnsureSteamTraits();
		base.LoadData(tag);
	}
}
