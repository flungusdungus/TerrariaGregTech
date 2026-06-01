#nullable enable
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Armor;

// Drains EU from worn GT armor on hit - port of ArmorLogicSuite.damageArmor.
// MP: per-stack ModItem state on a private inventory item; drain +
// charged-defense eval are owning-client-local (like MagnetItem). Fully
// server-authoritative armor-EU sync is deferred.
public sealed class GTArmorPlayer : ModPlayer
{
	public override void OnHurt(Player.HurtInfo info)
	{
		int dmg = info.Damage;
		if (dmg <= 0) return;
		// armor[0..2] = head/body/legs (3..9 are vanity/accessory).
		for (int i = 0; i <= 2; i++)
		{
			if (Player.armor[i]?.ModItem is GTArmorItem a)
				a.DrainOnHit(dmg);
		}
	}

	// Jetpack flight from an Advanced chestplate. DEVIATION: upstream
	// gates flight on a keybind + per-tick EU; we map "hold Jump" -> ascend.
	public override void PostUpdate()
	{
		if (Player.whoAmI != Main.myPlayer) return;
		if (Player.armor[1]?.ModItem is not GTArmorItem chest || !chest.FlightReady) return;
		if (!Player.controlJump || Player.mount.Active) return;

		const float MaxAscend = 7f;
		Player.velocity.Y = MathHelper.Lerp(Player.velocity.Y, -MaxAscend, 0.2f);
		Player.fallStart = (int)(Player.position.Y / 16f); // no fall damage while flying
		chest.DrainForFlight();
	}
}
