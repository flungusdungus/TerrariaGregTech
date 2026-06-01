#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Common.Machine.Trait.Hpca;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part.Hpca;
using Terraria.ModLoader.IO;
using RLStatus = GregTechCEuTerraria.Api.Machine.Feature.RecipeLogicStatus;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Electric.Research;

// Port of HPCAMachine (+ HPCAGridHandler). 3x3 grid of components (computation
// / cooler / bridge / empty). Per-tick: consume EU for allocated CWU/t, heat
// proportional to use, cooled by heat-sinks / active-coolers (active drain
// pcb_coolant from bound IN fluid hatches). Temp >=1000 -> random damage.
// IsRecipeLogicAvailable=false (driven by OnTick, RecipeType=DUMMY).
public class HPCAMachine : WorkableElectricMultiblockMachine, IOpticalComputationProvider, IControllable,
	Multiblock.IPowerDiagnostics
{
	long Multiblock.IPowerDiagnostics.PowerUpkeep   => _syncUpkeep;
	long Multiblock.IPowerDiagnostics.PowerCapacity => _syncCapacity;
	long Multiblock.IPowerDiagnostics.PowerStored   => _syncStored;
	long Multiblock.IPowerDiagnostics.PowerMaxInput => _syncMaxInput;

	private const double IDLE_TEMPERATURE   = 200;
	private const double DAMAGE_TEMPERATURE = 1000;

	private readonly HPCAGridHandler _hpcaHandler;
	private readonly List<NotifiableFluidTank> _coolantTanks = new();
	// Maintenance subsystem dormant - penalty = 0 today.
	private Api.Machine.Feature.Multiblock.IMaintenanceMachine? _maintenance;
	private bool   _hasNotEnoughEnergy;
	private double _temperature = IDLE_TEMPERATURE;

	// Position-seeded RNG so adjacent HPCAs don't share damage rolls.
	private Random? _rng;
	private Random Rng => _rng ??= new Random(unchecked(Position.X * 73856093 ^ Position.Y * 19349663));

	public HPCAMachine() : base()
	{
		_hpcaHandler = new HPCAGridHandler(this);
	}

	public override bool IsRecipeLogicAvailable() => false;

	public double Temperature => _temperature;
	public HPCAGridHandler Handler => _hpcaHandler;

	// Port of upstream `isActive` set by updateActive(). MUST be a real field,
	// NOT Recipe.IsActive(): recipe logic is dormant here (would return 0 forever
	// for GetMaxCWUt/RequestCWUt -> Research Station deadlock).
	private bool _isActive;
	public override bool IsActive => _isActive;

	// "Providing computation" - a powered HPCA with no consumer is genuinely idle.
	public override bool DisplayActive => DisplayCachedCWUt > 0;

	public override void OnStructureFormed()
	{
		base.OnStructureFormed();
		_energyContainer = GetEnergyContainer();
		_coolantTanks.Clear();
		_maintenance = null;
		var components = new List<HPCAComponentTrait>();
		foreach (var part in GetParts())
		{
			if (part is HPCAComponentPartMachine comp)
			{
				// Force bind - one-shot gather; if a grid part hasn't bound yet
				// its trait is null and silently skipped until next form.
				comp.BindDefinition();
				if (comp.ComponentTrait is { } t)
					components.Add(t);
			}
			if (part is FluidHatchPartMachine fh && fh.Io == IO.IN && fh.Tank is not null)
				_coolantTanks.Add(fh.Tank);
			if (_maintenance == null && part is Api.Machine.Feature.Multiblock.IMaintenanceMachine mm)
				_maintenance = mm;
		}
		_hpcaHandler.OnStructureForm(components);
		UpdateDisplaySnapshot();
	}

	// Snapshot of grid-handler state for MP clients (controller ticks server-only).
	// Upstream marks the handler @SyncToClient; we ride the scalars via SaveData.
	private int  _syncMaxCWUt, _syncCachedCWUt, _syncCoolDemand, _syncCoolAvail, _syncComp, _syncCool;
	private long _syncCachedEUt, _syncMaxEUt;
	private bool _syncHasBridge;
	private long _syncUpkeep, _syncStored, _syncCapacity, _syncMaxInput;

	public int  DisplayMaxCWUt    => _syncMaxCWUt;
	public int  DisplayCachedCWUt => _syncCachedCWUt;
	public long DisplayCachedEUt  => _syncCachedEUt;
	public long DisplayMaxEUt     => _syncMaxEUt;
	public int  DisplayCoolDemand => _syncCoolDemand;
	public int  DisplayCoolAvail  => _syncCoolAvail;
	public bool DisplayHasBridge  => _syncHasBridge;
	public int  DisplayCompCount  => _syncComp;
	public int  DisplayCoolCount  => _syncCool;
	public long DisplayUpkeep     => _syncUpkeep;
	public long DisplayStored     => _syncStored;
	public long DisplayCapacity   => _syncCapacity;
	public long DisplayMaxInput   => _syncMaxInput;

	private void UpdateDisplaySnapshot()
	{
		var g = _hpcaHandler;
		_syncMaxCWUt    = g.GetMaxCWUt();
		_syncCachedCWUt = g.CachedCWUt;
		_syncCachedEUt  = g.CachedEUt;
		_syncMaxEUt     = g.GetMaxEUt();
		_syncCoolDemand = g.GetMaxCoolingDemand();
		_syncCoolAvail  = g.GetMaxCoolingAmount();
		_syncHasBridge  = g.HasHPCABridge();
		_syncComp       = g.ComputationProviderCount;
		_syncCool       = g.CoolantProviderCount;
		_syncUpkeep     = g.GetCurrentEUt();
		var ec = _energyContainer ?? GetEnergyContainer();
		_syncStored   = ec?.EnergyStored ?? 0;
		_syncCapacity = ec?.EnergyCapacity ?? 0;
		// Capability (stable), not throughput - see ResearchPowerDiagnostics.
		_syncMaxInput = (ec?.InputVoltage ?? 0) * (ec?.InputAmperage ?? 0);
	}

	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["hpca_temp"]      = _temperature;
		tag["hpca_maxCwu"]    = _syncMaxCWUt;
		tag["hpca_cwu"]       = _syncCachedCWUt;
		tag["hpca_eu"]        = _syncCachedEUt;
		tag["hpca_maxEu"]     = _syncMaxEUt;
		tag["hpca_coolDem"]   = _syncCoolDemand;
		tag["hpca_coolAvail"] = _syncCoolAvail;
		tag["hpca_bridge"]    = _syncHasBridge;
		tag["hpca_comp"]      = _syncComp;
		tag["hpca_cool"]      = _syncCool;
		tag["hpca_upkeep"]    = _syncUpkeep;
		tag["hpca_stored"]    = _syncStored;
		tag["hpca_cap"]       = _syncCapacity;
		tag["hpca_maxin"]     = _syncMaxInput;
		tag["hpca_active"]    = _isActive;
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		if (tag.ContainsKey("hpca_temp"))      _temperature    = tag.GetDouble("hpca_temp");
		if (tag.ContainsKey("hpca_maxCwu"))    _syncMaxCWUt    = tag.GetInt("hpca_maxCwu");
		if (tag.ContainsKey("hpca_cwu"))       _syncCachedCWUt = tag.GetInt("hpca_cwu");
		if (tag.ContainsKey("hpca_eu"))        _syncCachedEUt  = tag.GetLong("hpca_eu");
		if (tag.ContainsKey("hpca_maxEu"))     _syncMaxEUt     = tag.GetLong("hpca_maxEu");
		if (tag.ContainsKey("hpca_coolDem"))   _syncCoolDemand = tag.GetInt("hpca_coolDem");
		if (tag.ContainsKey("hpca_coolAvail")) _syncCoolAvail  = tag.GetInt("hpca_coolAvail");
		if (tag.ContainsKey("hpca_bridge"))    _syncHasBridge  = tag.GetBool("hpca_bridge");
		if (tag.ContainsKey("hpca_comp"))      _syncComp       = tag.GetInt("hpca_comp");
		if (tag.ContainsKey("hpca_cool"))      _syncCool       = tag.GetInt("hpca_cool");
		if (tag.ContainsKey("hpca_upkeep"))    _syncUpkeep     = tag.GetLong("hpca_upkeep");
		if (tag.ContainsKey("hpca_stored"))    _syncStored     = tag.GetLong("hpca_stored");
		if (tag.ContainsKey("hpca_cap"))       _syncCapacity   = tag.GetLong("hpca_cap");
		if (tag.ContainsKey("hpca_maxin"))     _syncMaxInput   = tag.GetLong("hpca_maxin");
		if (tag.ContainsKey("hpca_active"))    _isActive       = tag.GetBool("hpca_active");
	}

	public override void OnStructureInvalid()
	{
		UpdateActive(false);
		base.OnStructureInvalid();
		_coolantTanks.Clear();
		_hpcaHandler.OnStructureInvalidate();
	}

	// === IOpticalComputationProvider =======================================

	public int RequestCWUt(int cwut, bool simulate, ICollection<IOpticalComputationProvider> seen)
	{
		seen.Add(this);
		return IsActive && IsWorkingEnabledFlag() && !_hasNotEnoughEnergy
			? _hpcaHandler.AllocateCWUt(cwut, simulate) : 0;
	}

	public int RequestCWUt(int cwut, bool simulate) => RequestCWUt(cwut, simulate, NewSeen());

	public int GetMaxCWUt(ICollection<IOpticalComputationProvider> seen)
	{
		seen.Add(this);
		if (!(IsActive && IsWorkingEnabledFlag())) return 0;
		// Server: live handler; client: handler ungathered, read synced mirror.
		return _hpcaHandler.ComponentCount > 0 ? _hpcaHandler.GetMaxCWUt() : _syncMaxCWUt;
	}

	public int GetMaxCWUt() => GetMaxCWUt(NewSeen());

	public bool CanBridge(ICollection<IOpticalComputationProvider> seen)
	{
		seen.Add(this);
		// don't show a problem if the structure is not yet formed
		if (!IsFormed) return true;
		return _hpcaHandler.ComponentCount > 0 ? _hpcaHandler.HasHPCABridge() : _syncHasBridge;
	}

	public bool CanBridge() => CanBridge(NewSeen());

	private static ICollection<IOpticalComputationProvider> NewSeen() => new HashSet<IOpticalComputationProvider>();

	private bool IsWorkingEnabledFlag() => GetRecipeLogic().IsWorkingEnabled();

	// === Tick ==============================================================

	protected override void OnTick()
	{
		base.OnTick();
		if (!IsServer) return;
		if (!IsFormed) return;
		Tick();
	}

	// Verbatim HPCAMachine.tick().
	private void Tick()
	{
		if (IsWorkingEnabledFlag()) ConsumeEnergy();
		if (IsActive)
		{
			// Force-cool at half-way to damage threshold.
			double midpoint = (DAMAGE_TEMPERATURE - IDLE_TEMPERATURE) / 2;
			double temperatureChange = _hpcaHandler.CalculateTemperatureChange(_coolantTanks, _temperature >= midpoint) / 2.0;
			if (_temperature + temperatureChange <= IDLE_TEMPERATURE)
				_temperature = IDLE_TEMPERATURE;
			else
				_temperature += temperatureChange;
			if (_temperature >= DAMAGE_TEMPERATURE)
				_hpcaHandler.AttemptDamageHPCA(Rng);
			_hpcaHandler.Tick();
		}
		else
		{
			_hpcaHandler.ClearComputationCache();
			_temperature = Math.Max(IDLE_TEMPERATURE, _temperature - 0.25);
		}
		if (_energyContainer is null) _energyContainer = GetEnergyContainer();
		UpdateActive(_energyContainer.EnergyStored > 0);
		UpdateDisplaySnapshot();
	}

	private void UpdateActive(bool active)
	{
		_isActive = active;
		foreach (var part in GetParts())
			if (part is HPCAComponentPartMachine comp)
				comp.ComponentTrait?.SetActive(active);
	}

	// Verbatim HPCAMachine.consumeEnergy().
	private void ConsumeEnergy()
	{
		if (_energyContainer is null) _energyContainer = GetEnergyContainer();
		long energyToConsume = _hpcaHandler.GetCurrentEUt();
		// +10% per maintenance problem - verbatim upstream.
		if (_maintenance != null)
			energyToConsume += _maintenance.GetNumMaintenanceProblems() * energyToConsume / 10;
		var logic = GetRecipeLogic();

		if (_hasNotEnoughEnergy && _energyContainer.GetInputPerSec() > 19L * energyToConsume)
			_hasNotEnoughEnergy = false;

		if (_energyContainer.EnergyStored >= energyToConsume)
		{
			if (!_hasNotEnoughEnergy)
			{
				long before = _energyContainer.EnergyStored;
				_energyContainer.ChangeEnergy(-energyToConsume);
				if (before - _energyContainer.EnergyStored == energyToConsume)
					logic.SetStatus(RLStatus.WORKING);
				else
				{
					_hasNotEnoughEnergy = true;
					logic.SetStatus(RLStatus.WAITING);
				}
			}
		}
		else
		{
			_hasNotEnoughEnergy = true;
			logic.SetStatus(RLStatus.WAITING);
		}
	}

	// Verbatim port of HPCAMachine.HPCAGridHandler.
	public sealed class HPCAGridHandler
	{
		private readonly HPCAMachine _controller;
		private readonly List<HPCAComponentTrait> _components = new();
		private readonly HashSet<HPCACoolantProviderTrait> _coolantProviders = new();
		private readonly HashSet<HPCAComputationProviderTrait> _computationProviders = new();
		private int _numBridges;
		private int _allocatedCWUt;
		public long CachedEUt;
		public int  CachedCWUt;

		public HPCAGridHandler(HPCAMachine controller) { _controller = controller; }

		public int  AllocatedCWUt => _allocatedCWUt;

		// Census accessors (diagnostics): how many components the controller
		// gathered, and how many of each provider kind.
		public int ComponentCount           => _components.Count;
		public int ComputationProviderCount => _computationProviders.Count;
		public int CoolantProviderCount     => _coolantProviders.Count;

		public void OnStructureForm(IEnumerable<HPCAComponentTrait> components)
		{
			Reset();
			foreach (var component in components)
			{
				_components.Add(component);
				if (component is HPCACoolantProviderTrait coolant) _coolantProviders.Add(coolant);
				if (component is HPCAComputationProviderTrait comp) _computationProviders.Add(comp);
				if (component.AllowBridging) _numBridges++;
			}
		}

		public void OnStructureInvalidate() => Reset();

		private void Reset()
		{
			ClearComputationCache();
			_components.Clear();
			_coolantProviders.Clear();
			_computationProviders.Clear();
			_numBridges = 0;
		}

		public void ClearComputationCache() => _allocatedCWUt = 0;

		public void Tick()
		{
			if (CachedCWUt != _allocatedCWUt) CachedCWUt = _allocatedCWUt;
			CachedEUt = GetCurrentEUt();
			if (_allocatedCWUt != 0) _allocatedCWUt = 0;
		}

		// Verbatim calculateTemperatureChange.
		public double CalculateTemperatureChange(List<NotifiableFluidTank> coolantTanks, bool forceCoolWithActive)
		{
			int maxCWUt = Math.Max(1, GetMaxCWUt());
			int maxCoolingDemand = GetMaxCoolingDemand();

			int temperatureIncrease = (int)Math.Round(1.0 * maxCoolingDemand * _allocatedCWUt / maxCWUt);

			long maxPassiveCooling = 0;
			long maxActiveCooling = 0;
			int maxCoolantDrain = 0;
			foreach (var coolantProvider in _coolantProviders)
			{
				if (coolantProvider.IsActiveCooler)
				{
					maxActiveCooling += coolantProvider.CoolingAmount;
					maxCoolantDrain += coolantProvider.MaxCoolantPerTick;
				}
				else
				{
					maxPassiveCooling += coolantProvider.CoolingAmount;
				}
			}

			double temperatureChange = temperatureIncrease - maxPassiveCooling;
			if (maxActiveCooling == 0 && maxCoolantDrain == 0)
				return temperatureChange;

			if (forceCoolWithActive || maxActiveCooling <= temperatureChange)
			{
				int remainingCoolant = maxCoolantDrain;
				remainingCoolant -= DrainCoolant(coolantTanks, remainingCoolant);
				if (remainingCoolant <= 0)
					temperatureChange -= maxActiveCooling;
				else
				{
					int coolantDrained = maxCoolantDrain - remainingCoolant;
					temperatureChange -= maxActiveCooling * (1.0 * coolantDrained / maxCoolantDrain);
				}
			}
			else if (temperatureChange > 0)
			{
				double temperatureToDecrease = Math.Min(temperatureChange, maxActiveCooling);
				int coolantToDrain = Math.Max(1, (int)(maxCoolantDrain * (temperatureToDecrease / maxActiveCooling)));
				int remainingCoolant = coolantToDrain;
				remainingCoolant -= DrainCoolant(coolantTanks, remainingCoolant);
				if (remainingCoolant <= 0)
					return 0;
				int coolantDrained = coolantToDrain - remainingCoolant;
				temperatureChange -= temperatureToDecrease * (1.0 * coolantDrained / coolantToDrain);
			}
			return temperatureChange;
		}

		// Drain up to `amount` mB of pcb_coolant from bound input tanks.
		private static int DrainCoolant(List<NotifiableFluidTank> tanks, int amount)
		{
			if (amount <= 0) return 0;
			int drained = 0;
			foreach (var tank in tanks)
			{
				for (int i = 0; i < tank.TankCount && drained < amount; i++)
				{
					var fluid = tank.GetFluidInTank(i);
					if (fluid.IsEmpty || fluid.Type!.Id != "pcb_coolant") continue;
					int want = amount - drained;
					var removed = tank.DrainInternal(new FluidStack(fluid.Type!, want), simulate: false);
					drained += removed.Amount;
				}
				if (drained >= amount) break;
			}
			return drained;
		}

		// Verbatim attemptDamageHPCA - 1/200 random damageable component.
		public void AttemptDamageHPCA(Random rng)
		{
			if (rng.Next(200) == 0)
			{
				var candidates = new List<HPCAComponentTrait>();
				foreach (var component in _components)
					if (component.CanBeDamaged) candidates.Add(component);
				if (candidates.Count > 0)
					candidates[rng.Next(candidates.Count)].SetDamaged(true);
			}
		}

		public int AllocateCWUt(int cwut, bool simulate)
		{
			if (cwut == 0) return 0;
			int maxCWUt = GetMaxCWUt();
			int availableCWUt = maxCWUt - _allocatedCWUt;
			int toAllocate = Math.Min(cwut, availableCWUt);
			if (!simulate) _allocatedCWUt += toAllocate;
			return toAllocate;
		}

		public int GetMaxCWUt()
		{
			int maxCWUt = 0;
			foreach (var c in _computationProviders) maxCWUt += c.GetCWUPerTick();
			return maxCWUt;
		}

		public long GetCurrentEUt()
		{
			long maximumCWUt = Math.Max(1, GetMaxCWUt());
			long maximumEUt = GetMaxEUt();
			long upkeepEUt = GetUpkeepEUt();
			if (maximumEUt == upkeepEUt) return maximumEUt;
			return upkeepEUt + ((maximumEUt - upkeepEUt) * _allocatedCWUt / maximumCWUt);
		}

		public long GetUpkeepEUt()
		{
			long upkeepEUt = 0;
			foreach (var c in _components) upkeepEUt += c.UpkeepEUt;
			return upkeepEUt;
		}

		public long GetMaxEUt()
		{
			long maximumEUt = 0;
			foreach (var c in _components) maximumEUt += c.MaxEUt;
			return maximumEUt;
		}

		public bool HasHPCABridge() => _numBridges > 0;

		public int GetMaxCoolingAmount()
		{
			int maxCooling = 0;
			foreach (var c in _coolantProviders) maxCooling += c.CoolingAmount;
			return maxCooling;
		}

		public int GetMaxCoolingDemand()
		{
			int maxCooling = 0;
			foreach (var c in _computationProviders) maxCooling += c.GetCoolingPerTick();
			return maxCooling;
		}

		public int GetMaxCoolantDemand()
		{
			int maxCoolant = 0;
			foreach (var c in _coolantProviders) maxCoolant += c.MaxCoolantPerTick;
			return maxCoolant;
		}
	}
}
