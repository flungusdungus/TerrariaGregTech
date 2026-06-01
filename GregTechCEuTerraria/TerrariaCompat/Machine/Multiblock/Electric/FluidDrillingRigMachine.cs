#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Electric;

// Adapted port of FluidDrillMachine. Upstream's chunk-keyed BedrockFluid vein
// system has no 2D analogue -> biome-keyed single fluid, no depletion (user
// spec). Per tick: drain V[tier] EU. Every CycleTicks (20): fill 1st bound
// output hatch with BaseProduction x RigMultiplier mB. Multipliers verbatim
// (MV=1, HV=16, EV=64). RecipeLogic stays dormant (DUMMY station).
public class FluidDrillingRigMachine : WorkableElectricMultiblockMachine
{
	private bool _isWorking;
	private int  _cycleProgress;
	private BiomeProbe.Biome _cachedBiome;
	private bool _biomeCached;
	private string _lastFluidId = "";
	private string _idleReason  = "";

	private NotifiableFluidTank? _fluidOut;

	public FluidDrillingRigMachine() : base() { }

	// RecipeType is a browser-display station; production runs from OnTick.
	public override bool IsRecipeLogicAvailable() => false;

	private const int CycleTicks = 20;

	private int RigMultiplier => Tier switch
	{
		VoltageTier.MV => 1,
		VoltageTier.HV => 16,
		VoltageTier.EV => 64,
		_              => 1,
	};

	// Upstream's tooltip shows mB x multiplier x 1.5 (pretty-print, not actual).
	private int BaseProduction => 1;

	private int ProductionPerCycle => BaseProduction * RigMultiplier;

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
		_fluidOut = null;
	}

	public override void OnPartUnload()
	{
		base.OnPartUnload();
		_fluidOut = null;
	}

	private void RebindIoParts()
	{
		_fluidOut = null;
		foreach (var part in GetParts())
		{
			if (part is FluidHatchPartMachine fh && fh.Io == IO.OUT && fh.Tank is not null)
			{
				_fluidOut = fh.Tank;
				return;
			}
		}
	}

	protected override void OnTick()
	{
		base.OnTick();
		if (!IsServer) return;
		if (!IsFormed) { ClearWorking("Structure not formed"); return; }
		if (GetMultiblockState().HasError()) { ClearWorking("Structure not formed"); return; }
		if (!WorkingEnabled) { ClearWorking("Disabled by player"); return; }

		if (_fluidOut == null) RebindIoParts();
		if (_fluidOut == null) { ClearWorking("Need an output fluid hatch"); return; }

		// Re-scans each cycle to catch terraforming (matches LargeMinerMachine).
		if (!_biomeCached)
		{
			_cachedBiome = BiomeProbe.GetForTile(Position.X, Position.Y);
			_biomeCached = true;
		}
		var fluid = BiomeWorldIOTables.GetFluid(_cachedBiome);
		if (fluid == null) { ClearWorking("No drillable fluid in biome"); return; }
		_lastFluidId = fluid.Id;

		// Buffer-full idle (no EU drain) matches upstream's stall behavior.
		var probe = new FluidStack(fluid, ProductionPerCycle);
		var simFill = _fluidOut.FillInternal(probe, simulate: true);
		if (simFill <= 0) { ClearWorking("Output hatch full"); return; }

		// EU drain.
		if (_energyContainer is null) _energyContainer = GetEnergyContainer();
		if (_energyContainer.EnergyStored < EuPerTick) { ClearWorking("Out of power"); return; }
		_energyContainer.ChangeEnergy(-EuPerTick);

		SetWorkingState(true, "");

		if (++_cycleProgress < CycleTicks) return;
		_cycleProgress = 0;
		_cachedBiome = BiomeProbe.GetForTile(Position.X, Position.Y);
		var produce = new FluidStack(fluid, ProductionPerCycle);
		_fluidOut.FillInternal(produce, simulate: false);
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

	// Skip WMM's recipe-shaped status (dormant logic prints duplicate "Idle").
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
			string fluidName = string.IsNullOrEmpty(_lastFluidId) ? "?" : _lastFluidId;
			lines.Add($"[c/55FF55:Drilling ({biome}):] {fluidName} {ProductionPerCycle}mB / {CycleTicks / 60.0:0.0}s");
		}
		else
		{
			lines.Add(string.IsNullOrEmpty(_idleReason)
				? "Idle"
				: $"[c/AAAAAA:Idle:] {_idleReason}");
		}
	}

	public override void SaveData(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.SaveData(tag);
		tag["fdr_working"]   = _isWorking;
		tag["fdr_progress"]  = _cycleProgress;
		tag["fdr_lastFluid"] = _lastFluidId;
	}

	public override void LoadData(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.LoadData(tag);
		_isWorking     = tag.GetBool("fdr_working");
		_cycleProgress = tag.GetInt("fdr_progress");
		_lastFluidId   = tag.GetString("fdr_lastFluid");
		// _biomeCached stays false -> first OnTick rebinds.
	}
}
