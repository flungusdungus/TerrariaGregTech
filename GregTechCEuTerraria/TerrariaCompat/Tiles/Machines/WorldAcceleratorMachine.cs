#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

// Adapted port of com.gregtechceu.gtceu.common.machine.electric.WorldAcceleratorMachine.
// Upstream accelerates randomTick-ing blocks in a 3D cube (+ a BlockEntity mode).
//
// DEVIATIONS (Terraria-adapted): cube -> square with side doubled
// to 4*tier+2 (LV 6x6 .. UV 34x34) to keep 2D per-tier coverage comparable;
// BlockEntity mode + screwdriver toggle DROPPED (no clean analogue - ambient
// automation lives in the random-tick path); dispatch via vanilla's private
// WorldGen.UpdateWorld_{Over,Under}groundTile (reflection) with
// checkNPCSpawns=false so it accelerates, doesn't spawn.
//
// Energy: receiver, capacity V[tier]*256, 8A in, draw 3*V[tier]
// (randomTickAmperage=3); no charger slot (upstream parity).
public sealed class WorldAcceleratorMachine : TieredEnergyMachine, IControllable
{
	public WorldAcceleratorMachine() { }
	public WorldAcceleratorMachine(VoltageTier tier) : base(tier) { }

	protected override string Label => Definition?.Label ?? "World Accelerator";

	// java:88 container = (V[tier]*256, V[tier], 8A) - 4x default cap + input amperage.
	public override bool CanAccept => true;
	public override long EnergyCapacity => VoltageTiers.Voltage(Tier) * 256L;

	protected override NotifiableEnergyContainer CreateEnergyContainer()
		=> NotifiableEnergyContainer.ReceiverContainer(EnergyCapacity, VoltageTiers.Voltage(Tier), 8);

	// randomTickAmperage = 3 -> 3 x V[tier] per tick.
	private long EnergyPerTick => 3L * VoltageTiers.Voltage(Tier);

	// 2x upstream's cubic side (tier<<1)+1 -> LV 6 .. UV 34.
	public int AreaSide => (((int)Tier << 1) + 1) << 1;

	// Upstream cubic success limits per tier (kept as-is; per-pick work is cheap).
	private static readonly int[] SuccessLimits = { 1, 8, 27, 64, 125, 216, 343, 512 };
	private int SuccessLimit
	{
		get { int t = (int)Tier; return SuccessLimits[Math.Clamp(t - 1, 0, SuccessLimits.Length - 1)]; }
	}

	// No charger slot; EnvironmentalExplosionTrait left enabled - both upstream parity.

	private bool _isWorkingEnabled = true;
	public bool IsWorkingEnabled() => _isWorkingEnabled;
	public void SetWorkingEnabled(bool enabled) => _isWorkingEnabled = enabled;

	private bool _active;
	public override bool IsActive => _active;

	// Reflection-cached vanilla random-tick dispatchers (private static, called
	// from WorldGen.UpdateWorld_Inner) - gives every per-tile growth case for free.
	private static readonly MethodInfo? _updateOverground = typeof(WorldGen).GetMethod(
		"UpdateWorld_OvergroundTile", BindingFlags.NonPublic | BindingFlags.Static);
	private static readonly MethodInfo? _updateUnderground = typeof(WorldGen).GetMethod(
		"UpdateWorld_UndergroundTile", BindingFlags.NonPublic | BindingFlags.Static);

	// Reused arg array - avoids per-call boxing allocations.
	private readonly object?[] _invokeArgs = new object?[4];

	private void RandomUpdateAt(int x, int y)
	{
		bool underground = y > (int)Main.worldSurface;
		var fn = underground ? _updateUnderground : _updateOverground;
		if (fn is null) return;
		_invokeArgs[0] = x;
		_invokeArgs[1] = y;
		_invokeArgs[2] = false;   // checkNPCSpawns - never spawn from acceleration
		_invokeArgs[3] = 0;       // wallDist - "near", same as on-screen
		fn.Invoke(null, _invokeArgs);
	}

	private static readonly Random Rng = new();

	protected override void OnTick()
	{
		bool canWork = _isWorkingEnabled && DrainEnergy(simulate: true);
		if (!canWork)
		{
			_active = false;
			return;
		}

		_active = true;
		DrainEnergy(simulate: false);

		// upstream update() shape: pick up to successLimit cells (cap
		// successLimit*3 attempts so a sparse area doesn't burn the tick).
		int side = AreaSide;
		int half = side / 2;
		int attempts = SuccessLimit * 3;
		int success = 0;
		int cx = Position.X + Size.Width / 2;
		int cy = Position.Y + Size.Height / 2;

		for (int i = 0; i < attempts && success < SuccessLimit; i++)
		{
			int x = cx + Rng.Next(side) - half;
			int y = cy + Rng.Next(side) - half;
			if (x < 0 || x >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY) continue;
			// Skip the machine's own footprint (upstream skips its own block).
			if (x >= Position.X && x < Position.X + Size.Width &&
			    y >= Position.Y && y < Position.Y + Size.Height) continue;
			RandomUpdateAt(x, y);
			success++;
		}
	}

	// simulate-first drain (java:150)
	private bool DrainEnergy(bool simulate)
	{
		long resultEnergy = EnergyContainer.EnergyStored - EnergyPerTick;
		if (resultEnergy >= 0L && resultEnergy <= EnergyContainer.EnergyCapacity)
		{
			if (!simulate) EnergyContainer.SetEnergyStored(resultEnergy);
			return true;
		}
		return false;
	}

	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["active"]           = _active;
		tag["isWorkingEnabled"] = _isWorkingEnabled;
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		_active           = tag.GetBool("active");
		_isWorkingEnabled = !tag.ContainsKey("isWorkingEnabled") || tag.GetBool("isWorkingEnabled");
	}

	public override void AppendTooltip(List<string> lines)
	{
		base.AppendTooltip(lines);
		lines.Add($"Area: {AreaSide}x{AreaSide} tiles around machine");
		lines.Add($"Picks: {SuccessLimit} / tick");
		lines.Add($"Draw: {EnergyPerTick:N0} EU/t");
		if (_active)
			lines.Add("Accelerating");
		else if (!_isWorkingEnabled)
			lines.Add("Disabled");
		else
			lines.Add("Idle: insufficient power");
	}
}
