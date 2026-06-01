#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine.Feature;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Common.Machine.Trait;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Steam;

// 1:1 port of SteamBoilerMachine. Adds waterTank on top of steamTank.
// Per-tick state machine: heating ratchets temp every 12t HP / 24t LP;
// idle + cooldown lapsed drops by CoolDownRate; every 10t at temp >= 100
// drains 1 mB water + fills steamTank; water-empty-while-hot = explode.
//
// Adaptations: particle effects dropped; subscription always-on + gated
// inside (vs upstream conditional subscribe - negligible perf drift);
// bucket interact lives on the UI side.
public abstract class SteamBoilerMachine : SteamWorkableMachine
{
	protected SteamBoilerMachine() : base() { }
	protected SteamBoilerMachine(bool isHighPressure) : base(isHighPressure) { }

	private NotifiableFluidTank? _waterTank;
	public NotifiableFluidTank WaterTank { get { EnsureSteamTraits(); return _waterTank!; } }

	protected virtual int WaterTankCapacity => 16_000;

	protected override void EnsureSteamTraits()
	{
		base.EnsureSteamTraits();
		if (_waterTank is not null) return;

		_waterTank = CreateWaterTank();
		_waterTank.SetFilter(fluid => !fluid.IsEmpty && fluid.Type!.Id == FluidRegistry.Water.Id);
		Traits.Attach(_waterTank);
		Traits.RegisterPersistent("WaterTank", _waterTank);
	}

	protected virtual NotifiableFluidTank CreateWaterTank() =>
		new(1, WaterTankCapacity, Api.Capability.Recipe.IO.IN);

	// Verbatim: boiler can't be switched off.
	public override bool SupportsWorkingEnabledToggle => false;

	public int CurrentTemperature { get; protected set; }
	protected int TimeBeforeCoolingDown { get; set; }
	protected bool HasNoWater { get; set; }

	// LP: 500/45t/1deg. HP: 1000/40t/1deg. Upstream ConfigHolder hardcoded here.
	public virtual int GetMaxTemperature()  => IsHighPressure ? 1000 : 500;
	protected virtual int GetCooldownInterval() => IsHighPressure ? 40 : 45;
	protected virtual int GetCoolDownRate() => 1;

	// SteamSolarBoiler overrides with a sunlight check.
	protected virtual bool IsHeating() => Recipe.IsWorking();

	/// <summary>LP solid: 240 mB / 10t at max temp. HP solid: 480. Override.</summary>
	protected abstract long GetBaseSteamOutput();

	/// <summary>Current steam output every 10 ticks. Scales linearly with temperature%.</summary>
	public long GetTotalSteamOutput()
	{
		if (CurrentTemperature < 100) return 0;
		return (long)(GetBaseSteamOutput() * ((float)CurrentTemperature / GetMaxTemperature()) / 2f);
	}

	public double TemperaturePercent => CurrentTemperature / (GetMaxTemperature() * 1.0);
	public float  TempProgress01     => (float)TemperaturePercent;

	// Verbatim: HP halves duration, LP is identity.
	public override Api.Recipe.GTRecipe? FullModifyRecipe(Api.Recipe.GTRecipe recipe)
	{
		if (!IsHighPressure) return recipe;
		return recipe.Copy(Api.Recipe.Content.ContentModifier.Multiplier_(0.5), modifyDuration: true);
	}

	// Kick to >=1 so steam starts immediately on fuel light-up.
	public override bool OnWorking()
	{
		bool value = base.OnWorking();
		if (CurrentTemperature < GetMaxTemperature())
		{
			CurrentTemperature = Math.Max(1, CurrentTemperature);
		}
		return value;
	}

	public override void AfterWorking()
	{
		base.AfterWorking();
		TimeBeforeCoolingDown = GetCooldownInterval();
	}

	// Steam production lives here, not in recipe IO (recipes are fuel-burn timers).
	protected override void OnTick()
	{
		base.OnTick();
		UpdateCurrentTemperature();
		TickAutoOutput();
	}

	private uint OffsetTimer => (uint)(Main.GameUpdateCount + (uint)(Position.X * 7 + Position.Y * 13));

	protected void UpdateCurrentTemperature()
	{
		if (IsHeating())
		{
			if (OffsetTimer % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(12) == 0)
			{
				if (CurrentTemperature < GetMaxTemperature())
				{
					if (IsHighPressure)
					{
						CurrentTemperature++;
					}
					else if (OffsetTimer % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(24) == 0)
					{
						CurrentTemperature++;
					}
				}
			}
		}
		else if (TimeBeforeCoolingDown == 0)
		{
			if (CurrentTemperature > 0)
			{
				CurrentTemperature = Math.Max(0, CurrentTemperature - GetCoolDownRate());
				TimeBeforeCoolingDown = GetCooldownInterval();
			}
		}
		else --TimeBeforeCoolingDown;

		if (OffsetTimer % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(10) == 0)
		{
			if (CurrentTemperature >= 100)
			{
				int fillAmount = (int)GetTotalSteamOutput();
				// DrainInternal - water tank is IO.IN, public Drain returns empty.
				bool hasDrainedWater = !WaterTank.DrainInternal(1, simulate: false).IsEmpty;
				long filledSteam = 0;
				if (hasDrainedWater)
				{
					filledSteam = SteamTank.FillInternal(
						new FluidStack(FluidRegistry.Steam, fillAmount), simulate: false);
				}
				if (this.HasNoWater && hasDrainedWater)
				{
					DoExplosion();
				}
				else
				{
					this.HasNoWater = !hasDrainedWater;
				}
				// Upstream vents 4 buckets of steam as a particle cloud; we void.
				if (filledSteam == 0 && hasDrainedWater)
				{
					SteamTank.Drain(4_000, simulate: false);
				}
			}
			else
			{
				this.HasNoWater = false;
			}
		}
	}

	// Verbatim GTUtil.doExplosion(level, pos, 2.0f).
	protected virtual void DoExplosion() =>
		EnvironmentalExplosionTrait.DoExplosionAt(this, 2.0f);

	// Upstream parity: bespoke subscription, no AutoOutputTrait.
	private const int AutoOutputPeriod = 5;
	private int _autoOutputCooldown;

	protected virtual void TickAutoOutput()
	{
		if (--_autoOutputCooldown > 0) return;
		_autoOutputCooldown = AutoOutputPeriod;
		AdjacentFluidPush.Push(this, sourceTankStart: SteamTankAbsoluteIndex,
			sourceTankCount: 1, maxAmount: 1000,
			side: IODirection.None, exclude: IODirection.Down);
	}

	protected virtual int WaterTankAbsoluteIndex => 0;
	protected virtual int SteamTankAbsoluteIndex => 1;

	public override int ResolveFluidTank(Api.Capability.Recipe.IO direction, int localIndex) =>
		direction == Api.Capability.Recipe.IO.IN ? WaterTankAbsoluteIndex + localIndex
		                                         : SteamTankAbsoluteIndex + localIndex;

	public override int TankCount => 2;
	public override FluidStack GetTank(int tank)
	{
		EnsureSteamTraits();
		return tank == WaterTankAbsoluteIndex
			? _waterTank!.GetFluidInTank(0)
			: SteamTank.GetFluidInTank(0);
	}
	public override int GetCapacity(int tank) =>
		tank == WaterTankAbsoluteIndex ? WaterTankCapacity : SteamTankCapacity;
	public override bool IsFluidValid(int tank, FluidStack fluid)
	{
		if (fluid.IsEmpty) return false;
		if (tank == WaterTankAbsoluteIndex) return fluid.Type!.Id == FluidRegistry.Water.Id;
		return false;
	}
	public override int Fill(FluidStack fluid, bool simulate)
	{
		if (fluid.IsEmpty) return 0;
		EnsureSteamTraits();
		if (fluid.Type!.Id == FluidRegistry.Water.Id)
			return _waterTank!.FillInternal(fluid, simulate);
		return 0;
	}
	public override FluidStack Drain(int maxAmount, bool simulate)
	{
		if (maxAmount <= 0) return FluidStack.Empty;
		EnsureSteamTraits();
		return SteamTank.Drain(maxAmount, simulate);
	}

	// Raw per-tank storage; storage's type filter still rejects wrong fluid on
	// IO-bypassed bucket clicks.
	public override IFluidHandler GetTankAccess(int tank)
	{
		EnsureSteamTraits();
		return tank == WaterTankAbsoluteIndex
			? _waterTank!.Storages[0]
			: SteamTank.Storages[0];
	}

	// Verbatim createUI:309/313 - water (true,false) blocks scooping mid-boil.
	public override (bool AllowFill, bool AllowDrain) GetTankClickCaps(int tank) =>
		tank == WaterTankAbsoluteIndex ? (true, false) : (false, true);
	public override FluidStack Drain(FluidStack fluid, bool simulate)
	{
		if (fluid.IsEmpty) return FluidStack.Empty;
		EnsureSteamTraits();
		return SteamTank.Drain(fluid, simulate);
	}

	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["currentTemperature"]    = CurrentTemperature;
		tag["timeBeforeCoolingDown"] = TimeBeforeCoolingDown;
		tag["hasNoWater"]            = HasNoWater;
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		CurrentTemperature    = tag.GetInt("currentTemperature");
		TimeBeforeCoolingDown = tag.GetInt("timeBeforeCoolingDown");
		HasNoWater            = tag.GetBool("hasNoWater");
	}

	public override void AppendTooltip(List<string> lines)
	{
		base.AppendTooltip(lines);
		lines.Add($"Temp: {CurrentTemperature}/{GetMaxTemperature()} ({TemperaturePercent * 100:F0}%)");
		var water = GetTank(WaterTankAbsoluteIndex);
		var steam = GetTank(SteamTankAbsoluteIndex);
		lines.Add($"Water: {(water.IsEmpty ? 0 : water.Amount):N0}/{WaterTankCapacity:N0} mB");
		lines.Add($"Steam: {(steam.IsEmpty ? 0 : steam.Amount):N0}/{SteamTankCapacity:N0} mB");
		// IsHeating-gated so idle boilers read 0 (surfaces brownouts).
		long perTick = IsHeating() ? GetTotalSteamOutput() / 10 : 0;
		lines.Add($"Output: {perTick:N0} mB/t");
	}
}
