#nullable enable
using System;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Machine;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Cover;

// Port of common.cover.CoverSolarPanel. Ticks free EU into the host while it
// can see the sun.
//
// Adaptations: GTUtil.canSeeSunClearly -> Terraria daytime + altitude check;
// getEnergyContainer -> cast CoverHolder (TieredEnergyMachine IS IEnergyContainer);
// acceptEnergyFromNetwork(null,...) -> AcceptEnergyFromNetwork(side,...).
//
// MUST use AcceptEnergyFromNetwork (NOT AddEnergy): for battery-distributed
// containers (BatteryBufferMachine's EnergyBatteryTrait), EnergyStored sums
// battery charge and _energyStored is just a rounding residual - AddEnergy
// writes the residual but never charges the batteries.
public sealed class CoverSolarPanel : CoverBehavior
{
	private readonly long _eut;
	private TickableSubscription? _subscription;

	public CoverSolarPanel(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide, long eut)
		: base(definition, coverHolder, attachedSide)
	{
		_eut = eut;
	}

	public override void OnLoad()
	{
		base.OnLoad();
		_subscription = CoverHolder.SubscribeServerTick(Update);
	}

	public override void OnRemoved()
	{
		base.OnRemoved();
		_subscription?.Unsubscribe();
		_subscription = null;
	}

	// Verbatim upstream: top face only + energy container required.
	public override bool CanAttach() =>
		base.CanAttach() && AttachedSide == CoverSide.Up && CoverHolder is IEnergyContainer;

	private void Update()
	{
		if (GetSkyStatus() == SkyStatus.Clear && CoverHolder is IEnergyContainer energyContainer)
			energyContainer.AcceptEnergyFromNetwork(
				WorldCapability.ToIODirection(AttachedSide), _eut, 1);
	}

	private enum SkyStatus { Clear, Night, Raining, Underground }

	// Daytime + not raining + above surface. Matches SolarPanelTileEntity:
	// column scan is too strict for roofed bases, sky-exposure is cheesable
	// from cavern depth; altitude is the cheap idiomatic Terraria gate.
	private SkyStatus GetSkyStatus()
	{
		if (!Main.dayTime) return SkyStatus.Night;
		if (Main.raining) return SkyStatus.Raining;
		if (CoverHolder.GetBlockPos().Y >= Main.worldSurface) return SkyStatus.Underground;
		return SkyStatus.Clear;
	}

	public override string? GetStatusText() => GetSkyStatus() switch
	{
		SkyStatus.Clear       => $"Producing {_eut} EU/t",
		SkyStatus.Night       => "Idle: nighttime",
		SkyStatus.Raining     => "Idle: raining",
		SkyStatus.Underground => "Idle: below the surface",
		_                     => null,
	};
}
