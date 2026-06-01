#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

// TERRARIA-COMPAT INVENTION - no upstream parity. Upstream's only "solar
// panel" is CoverSolarPanel (a cover pushing 1A EU/t into a host when sunlit).
// Until the cover system lands we ship a standalone TILE-form panel mirroring
// SimpleGeneratorMachine: emitter-container, pushes (V[tier], 1A) onto the cable
// net per tick when sunlit. Per-tick behavior matches CoverSolarPanel.update;
// only the attachment shape (standalone tile vs cover) differs.
public sealed class SolarPanelTileEntity : TieredEnergyMachine
{
	public SolarPanelTileEntity() { }
	public SolarPanelTileEntity(VoltageTier tier) : base(tier) { }

	protected override string  Label       => "Solar Panel";
	public override long EnergyCapacity => VoltageTiers.Voltage(Tier) * 64;

	// Emitter-mode container: outputs only (V[tier], 1A), never accepts EU.
	public override bool CanAccept  => false;
	public override bool CanExtract => true;

	// Port of upstream GTUtil.canSeeSunClearly (canSeeSky / isDay / !raining).
	// Terraria adaptation: canSeeSky -> altitude check (Position.Y < worldSurface)
	// rather than a per-tick column scan (can't be cheesed by a 1-wide shaft).
	private bool CanSeeSunClearly()
	{
		if (!Main.dayTime) return false;
		if (Main.raining) return false;
		return Position.Y < Main.worldSurface;
	}

	// Per-tick update (CoverSolarPanel.update shape): inject V[tier] into our own
	// container; the cable net's push side propagates it (1A at V[tier] max).
	protected override void OnTick()
	{
		if (!CanSeeSunClearly()) return;
		long produced = VoltageTiers.Voltage(Tier);  // V[tier] x 1A
		if (produced <= 0) return;
		EnergyContainer.SetEnergyStored(System.Math.Min(EnergyCapacity, EnergyContainer.EnergyStored + produced));
	}

	public override void AppendTooltip(List<string> lines)
	{
		base.AppendTooltip(lines);
		lines.Add(CanSeeSunClearly()
			? $"Producing: {VoltageTiers.Voltage(Tier):N0} EU/t (1A at {VoltageTiers.ShortName(Tier)})"
			: (Main.raining ? "Idle (rain)" : Main.dayTime ? "Idle (underground)" : "Idle (night)"));
	}
}
