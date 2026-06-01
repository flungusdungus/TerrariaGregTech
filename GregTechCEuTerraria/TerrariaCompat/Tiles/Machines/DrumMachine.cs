#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.Common.Machine.Trait;
using GregTechCEuTerraria.Common.Materials;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

// Port of com.gregtechceu.gtceu.common.machine.storage.DrumMachine.
// Single-fluid storage drum - one bounded tank, capacity fixed per material
// (16 buckets wooden .. 1024 tungstensteel), FLUID_PIPE-filtered (a wooden drum
// can't hold lava/gas). The machine declares IFluidHandler and forwards to the
// `cache` trait (WTM pattern). Auto-output locked to the DOWN face.
//
// Per-material, NOT per-tier: MachineDefinition is Tiered=false, so the id is
// the bare upstream id (wooden_drum, steel_drum, ...).
public sealed class DrumMachine : MetaMachine, IFluidHandler, IControllable
{
	public DrumMachine() { }
	public DrumMachine(VoltageTier tier) : base(tier) { }

	protected override string Label => Definition?.Label ?? "Drum";

	// Capacity (mB) from the definition - registerDrum's `capacity` arg.
	public int Capacity => Definition?.Capacity ?? 16_000;

	// upstream `cache` + `autoOutput`
	private NotifiableFluidTank? _cache;
	private AutoOutputTrait? _autoOutput;

	public NotifiableFluidTank Cache { get { EnsureTraits(); return _cache!; } }
	public override AutoOutputTrait? AutoOutput { get { EnsureTraits(); return _autoOutput; } }

	private void EnsureTraits()
	{
		if (_cache is not null) return;
		BindDefinition();

		// upstream: NotifiableFluidTank(1, cap, IO.BOTH).setFilter(FLUID_PIPE)
		_cache = new NotifiableFluidTank(1, Capacity, Api.Capability.Recipe.IO.BOTH);
		string? matId = Definition?.MaterialId;
		if (matId != null && MaterialRegistry.Get(matId)?.FluidPipe is IPropertyFluidFilter filter)
			_cache.SetFilter(filter.Test);
		Traits.Attach(_cache);
		Traits.RegisterPersistent("cache", _cache);

		_autoOutput = AutoOutputTrait.OfFluids(tankStart: 0, tankCount: 1);
		Traits.Attach(_autoOutput);
		Traits.RegisterPersistent("AutoOutput", _autoOutput);
		// Drum dumps DOWN only - direction locked by the validator.
		_autoOutput.SetFluidOutputDirection(IODirection.Down);
		_autoOutput.SetFluidOutputDirectionValidator(d => d == IODirection.Down);
	}

	protected override void OnMachineLoaded()
	{
		base.OnMachineLoaded();
		EnsureTraits();
	}

	// IFluidHandler - forwards to the `cache` trait (WTM pattern).
	public int TankCount => 1;
	public FluidStack GetTank(int tank) => Cache.GetFluidInTank(tank);
	public int GetCapacity(int tank) => Capacity;
	public bool IsFluidValid(int tank, FluidStack fluid) => Cache.IsFluidValid(tank, fluid);
	public int Fill(FluidStack resource, bool simulate) => Cache.Fill(resource, simulate);
	public FluidStack Drain(int maxAmount, bool simulate) => Cache.Drain(maxAmount, simulate);
	public FluidStack Drain(FluidStack fluid, bool simulate) => Cache.Drain(fluid, simulate);

	// Per-tank UI access (bucket/cell transfer) - raw tank, bypassing IO direction.
	public IFluidHandler GetTankAccess(int tank) => Cache.Storages[tank];

	public override bool SupportsAutoOutputItems  => false;
	public override bool SupportsAutoOutputFluids => true;

	// DrumLayout toggle + TankConfigSetAction.
	public bool IsAutoOutput
	{
		get => AutoOutput!.IsAutoOutputFluids;
		set => AutoOutput!.SetAllowAutoOutputFluids(value);
	}

	// IControllable - mirror of upstream's screwdriver auto-output toggle.
	// Field-only read: MetaMachine.WorkingEnabled is reached from ModifyLight on
	// FastParallel worker threads, where calling AutoOutput (lazy EnsureTraits)
	// would race. Setter is main-thread (screwdriver RMB / packet).
	bool IControllable.IsWorkingEnabled() => _autoOutput?.IsAutoOutputFluids ?? false;
	void IControllable.SetWorkingEnabled(bool enabled) => AutoOutput!.SetAllowAutoOutputFluids(enabled);
	public override bool SupportsWorkingEnabledToggle => false;

	// Portable data across break -> re-place (upstream saveToItem / loadFromItem).
	public override void WritePortableData(TagCompound tag)
	{
		var stored = Cache.Storages[0].Fluid;
		if (stored.IsEmpty) return;
		tag["fluidId"]     = stored.Type!.Id;
		tag["fluidAmount"] = stored.Amount;
		if (stored.Nbt != null) tag["fluidNbt"] = stored.Nbt;
	}

	public override void ReadPortableData(TagCompound tag)
	{
		if (tag.ContainsKey("fluidId") && FluidRegistry.TryGet(tag.GetString("fluidId"), out var type))
			Cache.Storages[0].SetFluid(new FluidStack(type, tag.GetInt("fluidAmount"),
				tag.ContainsKey("fluidNbt") ? tag.GetCompound("fluidNbt") : null));
	}

	// cache + autoOutput ride Traits.Save/Load.
	public override void SaveData(TagCompound tag) { EnsureTraits(); base.SaveData(tag); }
	public override void LoadData(TagCompound tag)
	{
		EnsureTraits();
		base.LoadData(tag);
		// Re-assert DOWN - output face is always locked, regardless of an older blob.
		_autoOutput!.SetFluidOutputDirection(IODirection.Down);
	}

	public override void AppendTooltip(List<string> lines)
	{
		base.AppendTooltip(lines);
		var stored = Cache.Storages[0].Fluid;
		lines.Add(stored.IsEmpty
			? $"Empty  (0 / {Capacity:N0} mB)"
			: $"{stored.Type!.DisplayName}: {stored.Amount:N0} / {Capacity:N0} mB");
		lines.Add("Right-click to open. Fill/drain through the fluid slot inside the UI");
	}
}
