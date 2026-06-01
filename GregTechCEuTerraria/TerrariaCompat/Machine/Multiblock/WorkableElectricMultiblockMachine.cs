#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine.Feature;
using GregTechCEuTerraria.Api.Machine.Multiblock;
using GregTechCEuTerraria.Api.Misc;
using GregTechCEuTerraria.Api.Recipe.Modifier;
using GregTechCEuTerraria.Common.Energy;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

// Port of WorkableElectricMultiblockMachine. Aggregates bound hatches into
// one EnergyContainerList; supplies IOverclockMachine for ELECTRIC_OVERCLOCK.
// Concrete (was abstract) - standard electric multis share via
// MachineFamily.MultiblockElectricStandard.
public class WorkableElectricMultiblockMachine : WorkableMultiblockMachine,
	ITieredMachine, IOverclockMachine
{
	protected override string Label => Definition?.Label ?? "Electric Multiblock";

	protected EnergyContainerList? _energyContainer;

	// Hatch-derived recipe-match cap (distinct from MetaMachine.Tier).
	public int MultiTier { get; protected set; }

	// MP clients (no _parts binding) read display values from this snapshot.
	public long DisplayEnergyStored   { get; private set; }
	public long DisplayEnergyCapacity { get; private set; }
	public long DisplayInputVoltage   { get; private set; }
	public long DisplayOutputVoltage  { get; private set; }
	public long DisplayInputAmperage  { get; private set; }
	public long DisplayOutputAmperage { get; private set; }

	public bool BatchEnabled { get; protected set; }

	public WorkableElectricMultiblockMachine() : base() { }

	public override void OnStructureInvalid()
	{
		base.OnStructureInvalid();
		_energyContainer = null;
		MultiTier = 0;
	}

	public override void OnStructureFormed()
	{
		base.OnStructureFormed();
		_energyContainer = GetEnergyContainer();
		MultiTier = VoltageTiers.FloorTierByVoltage(GetMaxVoltage());
		RefreshDisplaySnapshot();
	}

	public override void OnPartUnload()
	{
		base.OnPartUnload();
		_energyContainer = null;
		MultiTier = 0;
	}

	// Per-tick walk so a hatch swap re-aggregates without unform.
	private void RefreshDisplaySnapshot()
	{
		var ec = _energyContainer ?? GetEnergyContainer();
		DisplayEnergyStored   = ec.EnergyStored;
		DisplayEnergyCapacity = ec.EnergyCapacity;
		DisplayInputVoltage   = ec.InputVoltage;
		DisplayOutputVoltage  = ec.OutputVoltage;
		DisplayInputAmperage  = ec.InputAmperage;
		DisplayOutputAmperage = ec.OutputAmperage;
	}

	protected override void OnTick()
	{
		base.OnTick();
		// Aligns with 6-tick state-sync broadcast.
		if (IsServer && IsFormed && (Terraria.Main.GameUpdateCount & 0x7) == 0)
			RefreshDisplaySnapshot();
	}

	public IEnergyContainer GetDisplayEnergyContainer()
	{
		if (IsServer)
			return _energyContainer ?? GetEnergyContainer();
		return new MultiblockDisplayEnergyContainer(
			DisplayEnergyStored, DisplayEnergyCapacity,
			DisplayInputVoltage, DisplayOutputVoltage,
			DisplayInputAmperage, DisplayOutputAmperage);
	}

	public override void SetBatchEnabled(bool batch) { BatchEnabled = batch; }
	public override bool IsBatchEnabled() => BatchEnabled;

	public int OverclockTier => GetTier();
	public int MaxOverclockTier => GetTier();
	public int MinOverclockTier => GetTier();
	public void SetOverclockTier(int tier) { /* fixed at hatch tier */ }

	// Verbatim getOverclockVoltage. Generator multis (LargeCombustionEngine,
	// LargeTurbine) override with tier / rotor-power voltage sources.
	public virtual long OverclockVoltage
	{
		get
		{
			if (_energyContainer is null) _energyContainer = GetEnergyContainer();
			long voltage;
			long amperage;
			if (_energyContainer.InputVoltage > _energyContainer.OutputVoltage)
			{
				voltage  = _energyContainer.InputVoltage;
				amperage = _energyContainer.InputAmperage;
			}
			else
			{
				voltage  = _energyContainer.OutputVoltage;
				amperage = _energyContainer.OutputAmperage;
			}

			if (amperage == 1)
			{
				// Off-tier - floor to nearest lower tier (VEX for post-OC extended tiers).
				return VoltageTiers.VoltageEx(VoltageTiers.FloorTierByVoltage(voltage));
			}
			// On-tier - use as-is.
			return voltage;
		}
	}

	// IN-side first (consumers); falls back to OUT (generators).
	public EnergyContainerList GetEnergyContainer()
	{
		var containers = new List<IEnergyContainer>();
		var handlers = GetHandlersForCap(IO.IN, EURecipeCapability.CAP);
		if (handlers.Count == 0)
			handlers = GetHandlersForCap(IO.OUT, EURecipeCapability.CAP);
		foreach (var handler in handlers)
		{
			if (handler is IEnergyContainer container)
				containers.Add(container);
		}
		return new EnergyContainerList(containers);
	}

	private List<object> GetHandlersForCap(IO io, object cap)
	{
		if (CapabilitiesFlat.TryGetValue(io, out var inner) &&
		    inner.TryGetValue(cap, out var list))
			return list;
		return new List<object>();
	}

	// +1 tier bonus for multiple top-tier hatches (4xHV runs as EV).
	public virtual long GetMaxVoltage()
	{
		if (_energyContainer is null) _energyContainer = GetEnergyContainer();

		if (IsGenerator())
		{
			long voltage  = _energyContainer.OutputVoltage;
			long amperage = _energyContainer.OutputAmperage;
			if (amperage == 1)
				return VoltageTiers.VoltageEx(VoltageTiers.FloorTierByVoltage(voltage));
			return voltage;
		}
		else
		{
			long highestVoltage = _energyContainer.HighestInputVoltage;
			if (_energyContainer.NumHighestInputContainers > 1)
			{
				// +1 tier bounded by MAX; safe to use V[] here.
				int tier = VoltageTiers.TierByVoltage(highestVoltage);
				int capped = System.Math.Min(tier + 1, (int)VoltageTier.MAX);
				return VoltageTiers.V(capped);
			}
			return highestVoltage;
		}
	}

	public long GetDisplayRecipeVoltage()
	{
		var ec = GetEnergyContainer();
		return System.Math.Max(ec.HighestInputVoltage, ec.OutputVoltage);
	}

	public bool IsGenerator() => Definition?.IsGenerator ?? false;

	// Generator multis override to (int)MetaMachine.Tier - rotor holder needs
	// it for tierDifference math.
	public virtual int GetTier() => MultiTier;

	protected override void AppendEnergyLine(System.Collections.Generic.List<string> lines)
	{
		var ec = _energyContainer ??= GetEnergyContainer();
		long cap = ec.EnergyCapacity;
		if (cap <= 0) return;
		lines.Add($"Stored: {ec.EnergyStored:N0} / {cap:N0} EU");
	}

	public override long EnergyStored
	{
		get
		{
			if (_energyContainer is null) _energyContainer = GetEnergyContainer();
			return _energyContainer.EnergyStored;
		}
		set
		{
			if (_energyContainer is null) _energyContainer = GetEnergyContainer();
			long delta = value - _energyContainer.EnergyStored;
			if (delta != 0) _energyContainer.ChangeEnergy(delta);
		}
	}

	// PARALLEL_HATCH first, then def modifier (verbatim GCYMMachines).
	// PARALLEL_HATCH = IDENTITY when no parallel hatch.
	private RecipeModifier? _cachedModifier;
	public override RecipeModifier GetRecipeModifier()
	{
		if (_cachedModifier != null) return _cachedModifier;
		var defMod = Definition?.MultiRecipeModifier ?? RecipeModifier.NO_MODIFIER;
		_cachedModifier = new RecipeModifierList(
			Common.Recipe.GTRecipeModifiers.PARALLEL_HATCH, defMod);
		return _cachedModifier;
	}

	// Display fields ride SaveData so MP clients (no _parts) get correct values.
	public override void SaveData(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.SaveData(tag);
		tag["wemm_tier"] = MultiTier;
		tag["wemm_des"]  = DisplayEnergyStored;
		tag["wemm_dec"]  = DisplayEnergyCapacity;
		tag["wemm_div"]  = DisplayInputVoltage;
		tag["wemm_dov"]  = DisplayOutputVoltage;
		tag["wemm_dia"]  = DisplayInputAmperage;
		tag["wemm_doa"]  = DisplayOutputAmperage;
		tag["wemm_batch"] = BatchEnabled;
	}

	public override void LoadData(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.LoadData(tag);
		MultiTier              = tag.GetInt("wemm_tier");
		// wemm_des omitted from sync blob (synced via MachineEnergySyncPacket).
		// ContainsKey guard so a sync-blob load doesn't reset to 0.
		if (tag.ContainsKey("wemm_des"))
			DisplayEnergyStored = tag.GetLong("wemm_des");
		DisplayEnergyCapacity  = tag.GetLong("wemm_dec");
		DisplayInputVoltage    = tag.GetLong("wemm_div");
		DisplayOutputVoltage   = tag.GetLong("wemm_dov");
		DisplayInputAmperage   = tag.GetLong("wemm_dia");
		DisplayOutputAmperage  = tag.GetLong("wemm_doa");
		BatchEnabled           = tag.GetBool("wemm_batch");
	}

	// Periodic sync blob omits the per-tick DisplayEnergyStored - it rides
	// MachineEnergySyncPacket. Disk/join blob is unchanged.
	public override void SaveDataForSync(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.SaveDataForSync(tag);
		tag.Remove("wemm_des");
	}

	public override bool HasSyncEnergy => true;
	public override long SyncEnergyStored => DisplayEnergyStored;
	public override void ApplySyncEnergy(long energy) => DisplayEnergyStored = energy;
}
