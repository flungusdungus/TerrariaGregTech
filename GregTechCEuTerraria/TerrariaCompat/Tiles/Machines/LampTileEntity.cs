#nullable enable
using System;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Microsoft.Xna.Framework;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

// TERRARIA-COMPAT INVENTION - no upstream parity. Upstream's only "lamp" is
// LampBlock (a redstone-driven vanilla-style block, no EU/tier/entity). We ship
// a tier-templated lamp MACHINE instead: a receiver-mode TieredEnergyMachine
// drawing max(1, V[tier]/32) EU per tick, with IsActive following the brown-out
// flag (LampTile reads it for the lit state + brightness).
public sealed class LampTileEntity : TieredEnergyMachine
{
	public LampTileEntity() { }
	public LampTileEntity(VoltageTier tier) : base(tier) { }

	protected override string  Label       => "Lamp";

	// Receiver-mode container (input only).
	public override bool CanAccept => true;

	// Tight buffer (one second of EU at 1A); the 1/32x draw makes it last ~32s.
	public override long EnergyCapacity => VoltageTiers.Voltage(Tier);

	// 1/32 amp of tier voltage, floored to 1 EU/t (LV 1 / MV 4 / HV 16 / EV 64 / ...).
	public long DrawPerTick => Math.Max(1L, VoltageTiers.Voltage(Tier) / 32L);

	// Brown-out flag - true while last tick's draw was paid.
	private bool _isActive;
	public override bool IsActive => _isActive;

	protected override void OnTick()
	{
		long stored = EnergyContainer.EnergyStored;
		if (stored <= 0)
		{
			_isActive = false;
			return;
		}
		long draw = DrawPerTick;
		// Drain `draw`, OR the remainder if below `draw`. The remainder branch
		// matters under wire loss: refills cap at `voltage - loss`, so after N
		// full draws the buffer sits just below `draw`, where NEC's accept gate
		// (canAccept >= pathVoltage) rejects a refill. Draining to 0 reopens it.
		long actualDraw = System.Math.Min(stored, draw);
		EnergyContainer.SetEnergyStored(stored - actualDraw);
		_isActive = true;
	}

	// _isActive MUST ride SaveData - MachineStateSyncPacket syncs via Save/Load,
	// so without this an MP client's ModifyLight reads stale `false` and stays dark.
	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["lampActive"] = _isActive;
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		if (tag.ContainsKey("lampActive")) _isActive = tag.GetBool("lampActive");
	}

	// Per-tier brightness multiplier; color is the tier identity color
	// (VoltageTiers.LightColor). ULV = 1.0x (vanilla-torch parity), climbing up.
	private static readonly float[] _brightnessMultByTier =
	{
		1.00f, // ULV - vanilla torch parity (matches generic machine glow)
		1.50f, // LV
		1.90f, // MV
		2.20f, // HV
		2.50f, // EV
		2.80f, // IV
		3.00f, // LuV
		3.10f, // ZPM
		3.20f, // UV
		3.25f, // UHV
		3.30f, // UEV
		3.35f, // UIV
		3.40f, // UXV
		3.45f, // OpV
		3.50f, // MAX
	};

	public Vector3 LitColor
	{
		get
		{
			int idx = Math.Clamp((int)Tier, 0, _brightnessMultByTier.Length - 1);
			float m = _brightnessMultByTier[idx];
			return Common.Energy.VoltageTiers.LightColor(Tier) * m;
		}
	}

	public override Vector3 WorkingLightColor => LitColor;
}
