#nullable enable
using System;
using GregTechCEuTerraria.Api.Machine;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Net;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;

namespace GregTechCEuTerraria.Common.Machine.Trait;

// Port of com.gregtechceu.gtceu.common.machine.trait.EnvironmentalExplosionTrait.
//
// Per-machine sidecar that owns the "machine blows up" effect. Two jobs:
//   1. Presence-check + executor for over-voltage explosions. `NotifiableEnergy
//      Container.AcceptEnergyFromNetwork` looks the trait up via
//      `Machine.Traits.GetTrait(TYPE)` and calls `DoExplosion` when a cable
//      pushes voltage above the container's `InputVoltage`. No trait attached ->
//      no explosion (upstream parity - over-voltage is rejected silently).
//   2. Periodic environmental check - adjacent fluid (water/lava) explodes the
//      machine, rain sets it on fire, thunder makes it explode. The trait's
//      ticker subscribes on `OnMachineLoad`.
//
// Adaptations: CheckEnvironment is a no-op skeleton (the rain / adjacent-fluid
// checks need 2D-specific logic, deferred until the trait sees use; the per-tick
// subscription is left unattached). shouldWeatherOrTerrainExplosion config gate
// dropped (on whenever attached). GTUtil.doExplosion inlined as DoExplosion /
// DoExplosionAt (KillTile + smoke + bomb sound). Nothing persisted (defaults are
// attach-time, same as upstream).
public sealed class EnvironmentalExplosionTrait : MachineTrait
{
	public static readonly MachineTraitType<EnvironmentalExplosionTrait> TYPE = new();
	public override MachineTraitType TraitType => TYPE;

	public float ExplosionPower { get; set; }
	public float FireChance     { get; set; }
	public Func<bool> ExplosionPredicate { get; set; }

	private bool _enableEnvironmentalExplosions = true;

	public EnvironmentalExplosionTrait(float explosionPower, float fireChance, Func<bool> explosionPredicate)
		: base()
	{
		ExplosionPower      = explosionPower;
		FireChance          = fireChance;
		ExplosionPredicate  = explosionPredicate;
	}

	public EnvironmentalExplosionTrait(float explosionPower, float fireChance)
		: this(explosionPower, fireChance, () => true) { }

	public bool EnableEnvironmentalExplosions => _enableEnvironmentalExplosions;

	public void SetEnableEnvironmentalExplosions(bool value)
	{
		_enableEnvironmentalExplosions = value;
		// updateSubscription() - no-op for now since CheckEnvironment is a stub.
		// When CheckEnvironment is wired, gate SubscribeServerTick on this flag.
	}

	public override void OnMachineLoad()
	{
		base.OnMachineLoad();
		// updateSubscription() - left unattached. CheckEnvironment is a stub.
	}

	// === The shared "machine destroyed" effect ===============================
	// Called from:
	//   - NotifiableEnergyContainer.AcceptEnergyFromNetwork (over-voltage).
	//   - BatteryBufferMachine's energy trait (over-voltage - direct call,
	//     mirroring upstream which also bypasses the trait lookup there and
	//     calls GTUtil.doExplosion directly).
	//   - SteamBoilerMachine.DoExplosion (water-empty-while-hot).
	// `power` mirrors upstream's `explosionPower` arg to GTUtil.doExplosion;
	// today we don't model an actual radius - KillTile + sound + smoke is the
	// proportionate 2D presentation. The arg is preserved for parity and a
	// future blast-radius pass.
	public void DoExplosion(float power)
	{
		if (Machine is not MetaMachine mm) return;
		DoExplosionAt(mm, power);
	}

	// Static helper so over-voltage paths without the trait attached (or that
	// bypass it, mirroring upstream BatteryBufferMachine) share this presentation.
	//
	// MP correctness - three things must reach every client, none of which
	// WorldGen.KillTile does alone (vanilla relies on the caller to broadcast
	// TileManipulation, same as bombs at Projectile.cs:14230):
	//   1. Tile destruction - MessageID.TileManipulation action 0 (KillTile; 0
	//      lets each client resolve item drops, unlike action 4 = NoItem).
	//   2. Sound - SoundEngine.PlaySound is a no-op on a dedicated server, so we
	//      broadcast BlockExplosionEffectPacket to play it per-client.
	//   3. Smoke dust - same server no-op reasoning, folded into that packet.
	public static void DoExplosionAt(MetaMachine machine, float power)
	{
		var pos = machine.Position;
		int wTiles = machine.Size.Width;
		int hTiles = machine.Size.Height;

		// 1. Effect (sound + dust). Run locally so SP and the server-host's own
		//    client view see/hear it; broadcast the same effect to remote
		//    clients in MP.
		BlockExplosionEffectPacket.PlayLocal(pos.X, pos.Y, wTiles, hTiles);
		if (Main.netMode == NetmodeID.Server)
			BlockExplosionEffectPacket.Send(pos.X, pos.Y, wTiles, hTiles);

		// 2. Tile destruction. KillTile mutates the local world; in MP we
		//    additionally broadcast TileManipulation so every client mirrors
		//    the kill (same path vanilla bombs use).
		WorldGen.KillTile(pos.X, pos.Y, fail: false, effectOnly: false, noItem: false);
		if (Main.netMode != NetmodeID.SinglePlayer)
			NetMessage.SendData(MessageID.TileManipulation, -1, -1, null, 0, pos.X, pos.Y);
	}

	// Upstream's `GTUtil.getExplosionPower(voltage) = getTierByVoltage(voltage) + 1`.
	public static float GetExplosionPower(long voltage) =>
		VoltageTiers.TierByVoltage(voltage) + 1;
}
