#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.TerrariaCompat.Net;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Steam;

// Port of LargeBoilerMachine. Multi-tier (bronze/steel/titanium/tungstensteel)
// steam producer. Burns fuel -> heats up -> drains water from input hatches ->
// fills steam into output hatches. Per-tier (maxTemp, heatSpeed) on Definition.
// Throttle is player-controlled via BoilerThrottleSetAction (server-auth).
// Upstream's secondary-explosion grid dropped (primary KillTile only).
//
// DEVIATION: upstream's onStructureFormed/Invalid-
// scoped TickableSubscription collapses onto OnTick (every other multi here).
// The `tanksSeen == 0` guard stands in for the post-load rebind window
// (IsFormed persists across save, CapabilitiesFlat doesn't). Observable
// behavior identical to upstream.
public class LargeBoilerMachine : WorkableMultiblockMachine
{
	protected override string Label => Definition?.Label ?? "Large Boiler";

	private const int TicksPerSteamGeneration = 5;

	// Upstream config default (steamPerWater).
	public const int SteamPerWater = 150;

	public int CurrentTemperature { get; private set; }
	public int Throttle           { get; private set; } = 100;
	public int SteamGenerated     { get; private set; }

	public int MaxTemperature => Definition?.BoilerMaxTemperature ?? 800;
	public int HeatSpeed      => Definition?.BoilerHeatSpeed      ?? 4;

	public LargeBoilerMachine() : base() { }

	protected override RecipeLogic CreateRecipeLogic() => new LargeBoilerRecipeLogic();

	protected override void OnTick()
	{
		base.OnTick();
		if (!IsServer || !IsFormed) return;
		// MC-aligned timer - raw % N (see MetaMachine.GetMcOffsetTimer).
		long t = GetMcOffsetTimer();

		// Heat ramp: +HeatSpeed*10 every 10 MC-ticks while working, capped;
		// otherwise cool by CoolDownRate (mirror of updateCurrentTemperature).
		if (Recipe.IsWorking())
		{
			if (t % 10 == 0 && CurrentTemperature < MaxTemperature)
				CurrentTemperature = System.Math.Min(MaxTemperature, CurrentTemperature + HeatSpeed * 10);
		}
		else if (CurrentTemperature > 0)
		{
			CurrentTemperature -= GetCoolDownRate();
			if (CurrentTemperature < 0) CurrentTemperature = 0;
		}

		if (t % TicksPerSteamGeneration != 0) return;

		int maxDrain = CurrentTemperature * Throttle * TicksPerSteamGeneration / (SteamPerWater * 100);
		if (CurrentTemperature < 100)
		{
			SteamGenerated = 0;
			return;
		}
		if (maxDrain <= 0) return;

		// DrainInternal - boiler IS the recipe consumer, bypass CanCapOutput
		// (upstream tank.handleRecipe(IO.IN, ...)). Walks IO.IN + IO.BOTH.
		int waterRemaining = maxDrain;
		int tanksSeen = 0;
		foreach (var tank in CollectFluidTanks(IO.IN, IO.BOTH))
		{
			tanksSeen++;
			if (waterRemaining <= 0) break;
			var probe = new FluidStack(FluidRegistry.Water, waterRemaining);
			var drained = (tank is NotifiableFluidTank nft)
				? nft.DrainInternal(probe, simulate: false)
				: tank.Drain(probe, simulate: false);
			if (drained.IsEmpty) continue;
			waterRemaining -= drained.Amount;
		}
		int waterDrained = maxDrain - waterRemaining;
		SteamGenerated = waterDrained * SteamPerWater;

		// Post-load rebind window (~3 ticks): IsFormed persists but
		// CapabilitiesFlat hasn't been rebuilt - skip the water-starved
		// explosion check or every world-join would detonate.
		if (tanksSeen == 0) return;

		// FillInternal - boiler IS the recipe producer, bypass CanCapInput gate.
		if (waterDrained > 0)
		{
			int steamRemaining = SteamGenerated;
			foreach (var tank in CollectFluidTanks(IO.OUT, IO.BOTH))
			{
				if (steamRemaining <= 0) break;
				var probe = new FluidStack(FluidRegistry.Steam, steamRemaining);
				int filled = (tank is NotifiableFluidTank nft)
					? nft.FillInternal(probe, simulate: false)
					: tank.Fill(probe, simulate: false);
				if (filled <= 0) continue;
				steamRemaining -= filled;
			}
		}

		// Water-starved while hot.
		if (waterDrained < maxDrain)
			ExplodeBoiler();
	}

	// Walk IO.BOTH too - dual-hatch parts register only there.
	private IEnumerable<IFluidHandler> CollectFluidTanks(params IO[] directions)
	{
		foreach (var direction in directions)
		{
			if (!CapabilitiesFlat.TryGetValue(direction, out var byCap)) continue;
			if (!byCap.TryGetValue(FluidRecipeCapability.CAP, out var handlers)) continue;
			foreach (var h in handlers)
				if (h is IFluidHandler tank)
					yield return tank;
		}
	}

	protected virtual int GetCoolDownRate() => 1;

	// Mid-recipe rescale (upstream modifyFuelBurnTime).
	public void SetThrottle(int newThrottle)
	{
		newThrottle = System.Math.Clamp(newThrottle, 25, 100);
		if (newThrottle == Throttle) return;
		if (Recipe is LargeBoilerRecipeLogic lbrl)
			lbrl.OnThrottleChanged(Throttle, newThrottle);
		Throttle = newThrottle;
		if (IsServer) MachineStateSyncPacket.Broadcast(this);
	}

	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["lb_temp"] = CurrentTemperature;
		tag["lb_throttle"] = Throttle;
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		if (tag.ContainsKey("lb_temp"))     CurrentTemperature = tag.GetInt("lb_temp");
		if (tag.ContainsKey("lb_throttle")) Throttle           = tag.GetInt("lb_throttle");
		if (Throttle < 25 || Throttle > 100) Throttle = 100;
	}

	public override void NetSend(System.IO.BinaryWriter writer)
	{
		base.NetSend(writer);
		writer.Write((short)CurrentTemperature);
		writer.Write((byte)Throttle);
		writer.Write((short)SteamGenerated);
	}

	public override void NetReceive(System.IO.BinaryReader reader)
	{
		base.NetReceive(reader);
		CurrentTemperature = reader.ReadInt16();
		Throttle           = reader.ReadByte();
		SteamGenerated     = reader.ReadInt16();
	}

	private void ExplodeBoiler()
	{
		if (!IsServer) return;
		Common.Machine.Trait.EnvironmentalExplosionTrait.DoExplosionAt(this, 2.0f);
	}

	// Throttle scales fuel-burn duration (higher throttle -> faster -> more heat/sec).
	public class LargeBoilerRecipeLogic : RecipeLogic
	{
		private int _currentThrottle = 100;

		public override void SetupRecipe(Api.Recipe.GTRecipe recipe)
		{
			base.SetupRecipe(recipe);
			if (_lastRecipe != null && Machine is LargeBoilerMachine boiler)
			{
				_currentThrottle = boiler.Throttle;
				_duration = (int)System.Math.Round(_lastRecipe.Duration / (_currentThrottle / 100.0));
			}
		}

		// Verbatim modifyFuelBurnTime.
		public void OnThrottleChanged(int oldThrottle, int newThrottle)
		{
			if (_lastRecipe == null) return;
			double mult = (double)oldThrottle / newThrottle;
			_duration = (int)System.Math.Round(_lastRecipe.Duration / (newThrottle / 100.0));
			_progress = (int)System.Math.Round(_progress * mult);
			_currentThrottle = newThrottle;
		}

		// Else post-reload throttle changes rescale from the 100% baseline for one transition.
		public override void Save(TagCompound tag)
		{
			base.Save(tag);
			tag["currentThrottle"] = _currentThrottle;
		}

		public override void Load(TagCompound tag)
		{
			base.Load(tag);
			if (tag.ContainsKey("currentThrottle")) _currentThrottle = tag.GetInt("currentThrottle");
		}
	}
}
