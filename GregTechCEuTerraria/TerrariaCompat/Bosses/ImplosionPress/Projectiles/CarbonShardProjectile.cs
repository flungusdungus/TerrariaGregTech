#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Bosses.Common;
using Microsoft.Xna.Framework;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.ImplosionPress.Projectiles;

// Carbon shrapnel - the secondary bullet sprayed out from Crush Zone implosions,
// Mortar Salvo impacts, and Chain Reaction detonations. Sparse trail + cheap
// lighting because dense volleys are common. Palette set via ai[1]:
//   0 = warm-grey (default), 1 = dark-grey (Mortar), 2 = ITNT-red (Phase 2 / Diamond Forge).
// Arc-under-gravity ON (ai[0] = 1) for ejecta; straight for spray volleys.
// Reuses flint/gem + secondary for an angular shard silhouette.
public class CarbonShardProjectile : GlowingHostileProjectile
{
	private static readonly Color[] _palette =
	{
		new(180, 175, 170), // 0 warm grey - standard ejecta
		new(120, 115, 110), // 1 dark grey  - mortar / fuse-line shrapnel
		new(245, 95, 75),   // 2 ITNT red   - phase 2 / Diamond Forge ejecta
		new(230, 200, 110), // 3 ember gold - Detonator Press shockwave shrapnel
	};

	public override string Texture => "GregTechCEuTerraria/Content/Textures/item/material_sets/flint/gem";
	protected override string OverlayTexturePath => "GregTechCEuTerraria/Content/Textures/item/material_sets/flint/gem_secondary";
	protected override Color[] Palette => _palette;

	// Smoky trail + a low warm glow (the shrapnel is heated by detonation, not
	// the brightness of acid/frost). Sparse trail; staggered light.
	protected override Vector3 GlowColor => new(0.40f, 0.28f, 0.12f);
	protected override int TrailDustType => DustID.Smoke;
	protected override int HitDebuff => BuffID.OnFire;
	protected override int HitDebuffDuration => 180;
	protected override float SpinSpeed => 0.32f; // tumbling shrapnel
	protected override int TrailDustChance => 6;
	protected override int LightInterval => 3;
	protected override float ArcGravity => 0.18f;

	public override void SetDefaults()
	{
		base.SetDefaults();
		Projectile.width = 22;
		Projectile.height = 22;
		Projectile.scale = 1.8f;
		Projectile.timeLeft = 220;
	}
}
