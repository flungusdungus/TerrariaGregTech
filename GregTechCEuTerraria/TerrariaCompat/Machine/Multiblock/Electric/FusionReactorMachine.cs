#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Common.Energy;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Electric;

// Port of FusionReactorMachine. 3-tier (LuV/ZPM/UV). Capacitor trait
// (NotifiableEnergyContainer) capacity = inputHatches x 2^(tier-LuV) x 10M EU.
// UpdateHeat siphons from input hatches into capacitor; heat decays 10000/tick
// idle. FUSION_OC pays `eu_to_start - heat` from capacitor on recipe accept;
// OnWorking recovers heat lost during WAITING decay.
// Ring-color SyncToClient dropped (no 3D ring in 2D).
public class FusionReactorMachine : WorkableElectricMultiblockMachine
{
	private NotifiableEnergyContainer? _capacitor;

	// Cached aggregate so the heat loop can siphon directly.
	private Api.Misc.EnergyContainerList? _inputEnergyContainers;

	private long _heat;

	public long Heat
	{
		get => _heat;
		set => _heat = value;
	}

	public NotifiableEnergyContainer CapacitorContainer => _capacitor ??= EnsureCapacitor();

	public FusionReactorMachine() : base() { }

	// Capacity = 0 until OnStructureFormed reads the input-hatch count.
	private NotifiableEnergyContainer EnsureCapacitor()
	{
		BindDefinition();
		var cap = new NotifiableEnergyContainer(0, 0, 0, 0, 0);
		Traits.Attach(cap);
		Traits.RegisterPersistent("fusion_capacitor", cap);
		return cap;
	}

	protected override void OnTick()
	{
		_capacitor ??= EnsureCapacitor();
		base.OnTick();

		if (IsServer && IsFormed && ShouldRunHeatLoop())
			UpdateHeat();
	}

	// No-op - our loop is per-tick gated.
	public void UpdatePreHeatSubscription() { }

	private bool ShouldRunHeatLoop()
	{
		if (_heat > 0) return true;
		if (_inputEnergyContainers is null) return false;
		var cap = _capacitor;
		if (cap is null) return false;
		return _inputEnergyContainers.EnergyStored > 0 && cap.EnergyStored < cap.EnergyCapacity;
	}

	public override void OnStructureFormed()
	{
		base.OnStructureFormed();
		_capacitor ??= EnsureCapacitor();

		_inputEnergyContainers = GetEnergyContainer();
		int inputCount = CountInputEnergyContainers();

		_capacitor.ResetBasicInfo(
			maxCapacity:       CalculateEnergyStorageFactor(GetTier(), inputCount),
			maxInputVoltage:   0, maxInputAmperage:  0,
			maxOutputVoltage:  0, maxOutputAmperage: 0);
		UpdatePreHeatSubscription();
	}

	public override void OnStructureInvalid()
	{
		base.OnStructureInvalid();
		_inputEnergyContainers = null;
		_heat = 0;
		if (_capacitor is not null)
		{
			_capacitor.ResetBasicInfo(0, 0, 0, 0, 0);
			_capacitor.SetEnergyStored(0);
		}
		UpdatePreHeatSubscription();
	}

	private int CountInputEnergyContainers()
	{
		if (!CapabilitiesFlat.TryGetValue(IO.IN, out var inner)) return 0;
		if (!inner.TryGetValue(EURecipeCapability.CAP, out var list)) return 0;
		int n = 0;
		foreach (var h in list) if (h is IEnergyContainer) n++;
		return n;
	}

	public override bool OnWorking()
	{
		var recipe = Recipe.GetLastRecipe();
		if (recipe is not null)
		{
			// Type-tolerant - GetLong throws on int-stored values.
			long euToStart = Common.Recipe.GTRecipeModifiers.ReadDataLong(recipe.Data, "eu_to_start");
			if (euToStart > 0)
			{
				long heatDiff = euToStart - _heat;
				if (heatDiff > 0)
				{
					Recipe.SetWaiting("gtceu.recipe_logic.insufficient_fuel");
					if (CapacitorContainer.EnergyStored < heatDiff) return base.OnWorking();

					CapacitorContainer.RemoveEnergy(heatDiff);
					_heat += heatDiff;
					UpdatePreHeatSubscription();
				}
			}
		}
		return base.OnWorking();
	}

	// Verbatim updateHeat.
	private void UpdateHeat()
	{
		// Decay only when genuinely idle / paused / waiting@0 (don't punish a near-complete recipe).
		bool noProgress = Recipe.IsWaiting() && Recipe.GetProgress() == 0;
		if (((Recipe.IsIdle()) || (!Recipe.IsWorkingEnabled()) || noProgress) && _heat > 0)
		{
			_heat = _heat <= 10000 ? 0 : (_heat - 10000);
		}

		var cap = _capacitor;
		if (cap is null || _inputEnergyContainers is null) return;
		long leftStorage = cap.EnergyCapacity - cap.EnergyStored;
		if (leftStorage > 0)
		{
			// EnergyContainerList exposes only ChangeEnergy; negate to recover RemoveEnergy.
			long drained = -_inputEnergyContainers.ChangeEnergy(-leftStorage);
			cap.AddEnergy(drained);
		}
		UpdatePreHeatSubscription();
	}

	// FUSION_OC reads getMaxVoltage directly.
	public override long GetMaxVoltage() =>
		System.Math.Min(VoltageTiers.V(GetTier()), base.GetMaxVoltage());

	// Intrinsic design tier (generator-multi convention).
	public override int GetTier() => (int)Tier;

	// LuV: 10M/hatch ... UV: 40M/hatch. x16 hatches = 160M..640M.
	public static long CalculateEnergyStorageFactor(int tier, int energyInputAmount) =>
		(long)energyInputAmount * (long)System.Math.Pow(2, tier - (int)VoltageTier.LuV) * 10_000_000L;

	public override void AppendTooltip(System.Collections.Generic.List<string> lines)
	{
		base.AppendTooltip(lines);
		if (!IsFormed) return;
		var cap = _capacitor;
		if (cap is null || cap.EnergyCapacity <= 0) return;
		lines.Add($"Capacitor: {cap.EnergyStored:N0} / {cap.EnergyCapacity:N0} EU");
		lines.Add($"Heat: {_heat:N0} EU");
	}

	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["fusion_heat"] = _heat;
	}

	public override void LoadData(TagCompound tag)
	{
		// Ensure trait BEFORE base.LoadData runs Traits.Load - else accumulated
		// startup EU is dropped on reload.
		_capacitor ??= EnsureCapacitor();
		base.LoadData(tag);
		if (tag.ContainsKey("fusion_heat")) _heat = tag.GetLong("fusion_heat");
	}
}
