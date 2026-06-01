#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Bosses.Common;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.ImplosionPress.Projectiles;

// ITNT-themed projectile for the Hellblast Volley - fast, penetrating, glowing
// red. Inspired by Calamity's BrimstoneHellblast pattern (dense fast spread). No
// helix particle trail in v1 (visual-only flair; add later if the volley reads
// too plain). Palette via ai[1]:
//   0 = ITNT red (default phase 1), 1 = white-hot (phase 2).
public class HellblastOrbProjectile : GlowingHostileProjectile
{
	private static readonly Color[] _palette =
	{
		new(245, 95, 75),   // 0 ITNT red - phase 1
		new(255, 220, 200), // 1 white-hot - phase 2
	};

	public override string Texture => "GregTechCEuTerraria/Content/Textures/item/material_sets/ruby/gem";
	protected override string OverlayTexturePath => "GregTechCEuTerraria/Content/Textures/item/material_sets/ruby/gem_overlay";
	protected override Color[] Palette => _palette;

	// Bright red-orange self-glow + sparse smoky trail.
	protected override Vector3 GlowColor => new(0.85f, 0.30f, 0.15f);
	protected override int TrailDustType => DustID.Torch;
	protected override int HitDebuff => BuffID.OnFire;
	protected override int HitDebuffDuration => 240;
	protected override float SpinSpeed => 0.18f; // smooth spin, not a tumble
	protected override int TrailDustChance => 4;
	protected override int LightInterval => 2;

	public override void SetDefaults()
	{
		base.SetDefaults();
		// Penetrating + tile-passing - it's a sustained pressure pattern, not
		// terrain-aware. Capped life keeps the pool clean.
		Projectile.tileCollide = false;
		Projectile.penetrate = -1;
		Projectile.timeLeft = 180;
		Projectile.width = 28;
		Projectile.height = 28;
		Projectile.scale = 1.6f;
	}

	public override void AI()
	{
		// Skip base AI's arc-gravity even though ai[0] should be 0 for hellblasts -
		// the orb is straight-flight by design. Self-accelerate slightly each tick
		// (Brimstone-Hellblast-style) so the spread fans outward.
		float dir = System.Math.Sign(Projectile.velocity.X);
		if (dir == 0f) dir = 1f;
		Projectile.rotation += SpinSpeed * dir;

		if (Projectile.velocity.Length() < MaxAccelSpeed)
			Projectile.velocity *= AccelMul;

		if (LightInterval <= 1 ||
		    (Main.GameUpdateCount + (uint)Projectile.identity) % (uint)LightInterval == 0)
			Lighting.AddLight(Projectile.Center, GlowColor.X, GlowColor.Y, GlowColor.Z);

		if (TrailDustChance > 0 && Main.rand.NextBool(TrailDustChance))
		{
			var d = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height,
				TrailDustType, 0f, 0f, 100, default, 1f);
			d.noGravity = true;
			d.velocity *= 0.3f;
		}
	}

	private const float AccelMul = 1.025f;       // per-tick speed multiplier
	private const float MaxAccelSpeed = 18f;     // cap on accelerated speed (px/tick)
}
