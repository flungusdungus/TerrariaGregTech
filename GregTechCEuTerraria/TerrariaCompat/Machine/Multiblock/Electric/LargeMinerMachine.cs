#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;
using Terraria;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Electric;

// Adapted port of LargeMinerMachine. 3D chunk-walker -> biome-keyed lottery
// (BiomeWorldIOTables). Per tick: V[tier] EU + drilling_fluid gate. Every
// CycleTicks: drain DrillingFluidPerCycle mB + push OutputCount raw_ore to
// first OUT bus. RecipeLogic dormant (IsRecipeLogicAvailable=false).
public class LargeMinerMachine : WorkableElectricMultiblockMachine
{
	private bool   _isWorking;
	private int    _cycleProgress;
	private BiomeProbe.Biome _cachedBiome;
	private bool   _biomeCached;
	private string _lastOreId = "";
	private string _idleReason = "";

	private NotifiableFluidTank?         _drillingFluidIn;
	private NotifiableItemStackHandler?  _oreOut;

	// Position-seeded so adjacent miners desync.
	private Random? _rng;
	private Random Rng => _rng ??= new Random(unchecked(Position.X * 73856093 ^ Position.Y * 19349663));

	public LargeMinerMachine() : base() { }

	// Recipe type is browser-display only; production runs from OnTick.
	public override bool IsRecipeLogicAvailable() => false;

	// EV=10s / IV=7.5s / LuV=5s (upstream's 64/tier per-block speed).
	private int CycleTicks => Tier switch
	{
		VoltageTier.EV  => 200,
		VoltageTier.IV  => 150,
		VoltageTier.LuV => 100,
		_               => 200,
	};

	// EV=1, IV=2, LuV=4.
	private int OutputCount => Tier switch
	{
		VoltageTier.EV  => 1,
		VoltageTier.IV  => 2,
		VoltageTier.LuV => 4,
		_               => 1,
	};

	// Upstream's 8-(tier-5) per-tick -> 4/3/2 per cycle.
	private int DrillingFluidPerCycle => Tier switch
	{
		VoltageTier.EV  => 4,
		VoltageTier.IV  => 3,
		VoltageTier.LuV => 2,
		_               => 4,
	};

	// VA[tier] = V x 15/16, NOT V[tier].
	private long EuPerTick => VoltageTiers.VA((int)Tier);

	public override void OnStructureFormed()
	{
		base.OnStructureFormed();
		RebindIoParts();
	}

	public override void OnStructureInvalid()
	{
		base.OnStructureInvalid();
		_drillingFluidIn = null;
		_oreOut = null;
	}

	public override void OnPartUnload()
	{
		base.OnPartUnload();
		_drillingFluidIn = null;
		_oreOut = null;
	}

	private void RebindIoParts()
	{
		_drillingFluidIn = null;
		_oreOut = null;
		foreach (var part in GetParts())
		{
			if (_drillingFluidIn == null && part is FluidHatchPartMachine fh
				&& fh.Io == IO.IN && fh.Tank is not null)
				_drillingFluidIn = fh.Tank;
			if (_oreOut == null && part is ItemBusPartMachine ib
				&& ib.Io == IO.OUT && ib.Inventory is not null)
				_oreOut = ib.Inventory;
			if (_drillingFluidIn != null && _oreOut != null) return;
		}
	}

	protected override void OnTick()
	{
		base.OnTick();
		if (!IsServer) return;
		if (!IsFormed) { ClearWorking("Structure not formed"); return; }
		if (GetMultiblockState().HasError()) { ClearWorking("Structure not formed"); return; }
		if (!WorkingEnabled) { ClearWorking("Disabled by player"); return; }

		if (_drillingFluidIn == null || _oreOut == null) RebindIoParts();
		if (_oreOut == null) { ClearWorking("Need an output bus"); return; }

		if (_energyContainer is null) _energyContainer = GetEnergyContainer();
		if (_energyContainer.EnergyStored < EuPerTick)
		{
			ClearWorking("Out of power");
			return;
		}
		_energyContainer.ChangeEnergy(-EuPerTick);

		// Cycle model: gate on stored fluid, drain at production time.
		if (_drillingFluidIn == null || GetDrillingFluidStored() < DrillingFluidPerCycle)
		{
			ClearWorking("Need drilling fluid");
			return;
		}

		SetWorkingState(true, "");

		if (++_cycleProgress < CycleTicks) return;
		_cycleProgress = 0;
		HandleProductionCycle();
	}

	private void HandleProductionCycle()
	{
		// Re-scan per cycle to catch terraforming.
		_cachedBiome = BiomeProbe.GetForTile(Position.X, Position.Y);
		_biomeCached = true;

		if (_drillingFluidIn == null) return;
		var drillingFluid = FluidRegistry.Get("drilling_fluid");
		if (drillingFluid == null) return;
		var drained = _drillingFluidIn.DrainInternal(
			new FluidStack(drillingFluid, DrillingFluidPerCycle), simulate: false);
		if (drained.IsEmpty || drained.Amount < DrillingFluidPerCycle) return;

		var (itemType, matId) = BiomeWorldIOTables.RollOre(_cachedBiome, Rng);
		if (itemType <= 0) return;
		_lastOreId = matId;

		// Overflow is silently lost (no chunk walker for LootContext overflow).
		if (_oreOut == null) return;
		var leftover = OutputCount;
		for (int slot = 0; slot < _oreOut.SlotCount && leftover > 0; slot++)
		{
			var stack = new Item();
			stack.SetDefaults(itemType);
			stack.stack = leftover;
			var rem = _oreOut.InsertInternal(slot, stack, simulate: false);
			leftover = rem.stack;
		}
	}

	private int GetDrillingFluidStored()
	{
		if (_drillingFluidIn == null) return 0;
		var stack = _drillingFluidIn.Storages[0].Fluid;
		var df = FluidRegistry.Get("drilling_fluid");
		if (df == null || stack.IsEmpty || stack.Type?.Id != df.Id) return 0;
		return stack.Amount;
	}

	private void ClearWorking(string reason)
	{
		_cycleProgress = 0;
		SetWorkingState(false, reason);
	}

	private void SetWorkingState(bool working, string reason)
	{
		if (_isWorking == working && _idleReason == reason) return;
		_isWorking = working;
		_idleReason = reason;
	}

	public override bool IsActive => _isWorking;

	// Skip WMM's recipe-shaped status (dormant logic would duplicate "Idle").
	public override void AppendTooltip(List<string> lines)
	{
		lines.Add(DisplayName);
		OnAddFancyInformationTooltip(lines);

		if (!IsFormed)
		{
			AppendUnformedStructureBlock(lines);
			return;
		}

		if (_isWorking)
		{
			string biome = _biomeCached ? _cachedBiome.ToString() : "scanning";
			if (!string.IsNullOrEmpty(_lastOreId))
				lines.Add($"[c/55FF55:Mining ({biome}):] {_lastOreId} x{OutputCount} / {CycleTicks / 60.0:0.0}s");
			else
				lines.Add($"[c/55FF55:Mining ({biome}):] warming up");
		}
		else
		{
			lines.Add(string.IsNullOrEmpty(_idleReason)
				? "Idle"
				: $"[c/AAAAAA:Idle:] {_idleReason}");
		}
	}

	// Biome re-scans next cycle, not persisted.
	public override void SaveData(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.SaveData(tag);
		tag["lm_working"]   = _isWorking;
		tag["lm_progress"]  = _cycleProgress;
		tag["lm_lastOre"]   = _lastOreId;
	}

	public override void LoadData(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.LoadData(tag);
		_isWorking     = tag.GetBool("lm_working");
		_cycleProgress = tag.GetInt("lm_progress");
		_lastOreId     = tag.GetString("lm_lastOre");
		// _biomeCached stays false -> first OnTick re-scans.
	}
}
