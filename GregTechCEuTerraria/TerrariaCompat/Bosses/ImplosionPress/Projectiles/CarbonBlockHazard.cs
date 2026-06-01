#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.ImplosionPress.Projectiles;

// The muffler's lobbed carbon arcs. Two-stage projectile:
//
//   stage 0 (ai[0] == 0): falls under gravity, contact-damages, lands on tile
//                          -> transitions to stage 1 by snapping velocity to zero.
//   stage 1 (ai[0] == 1): stationary block, contact-damages for LifeAfterLandTicks
//                          ticks then fades out in the last FadeTicks.
//
// Arena slowly fills with these (subject to the boss's FIFO cap) so by minute 2
// the floor is a hazard maze - persistent pressure between attacks.
//
//   ai[0] = stage (0 = falling, 1 = landed)
//   ai[1] = ticks since landing (drives the fade)
public class CarbonBlockHazard : ModProjectile
{
	// ---- tunables ----------------------------------------------------------
	private const int LifeAfterLandTicks = 480; // 8s at 60fps
	private const int FadeTicks = 45;
	private const float Gravity = 0.42f;
	private const float MaxFallSpeed = 14f;
	private const int ContactDamageReduction = 60; // dust-puff damage feedback only

	public override string Texture => "GregTechCEuTerraria/Content/Textures/item/material_sets/lignite/gem";

	public override void SetDefaults()
	{
		Projectile.width = 24;
		Projectile.height = 24;
		Projectile.hostile = true;
		Projectile.friendly = false;
		Projectile.tileCollide = true;
		Projectile.penetrate = -1;
		Projectile.timeLeft = LifeAfterLandTicks * 4; // generous - real death is via ai[1] post-land
		Projectile.aiStyle = -1;
		Projectile.ignoreWater = true;
	}

	public override bool? CanDamage()
	{
		// Stop hurting once it starts fading, so the visual fade reads as "safe now".
		if (Projectile.ai[0] == 1f && Projectile.ai[1] > LifeAfterLandTicks - FadeTicks)
			return false;
		return null;
	}

	public override void AI()
	{
		if (Projectile.ai[0] == 0f)
		{
			// Falling.
			Projectile.velocity.Y += Gravity;
			if (Projectile.velocity.Y > MaxFallSpeed) Projectile.velocity.Y = MaxFallSpeed;
			Projectile.rotation += 0.06f * Math.Sign(Projectile.velocity.X != 0f ? Projectile.velocity.X : 1f);
		}
		else
		{
			// Landed - count up to fade.
			Projectile.velocity = Vector2.Zero;
			Projectile.ai[1]++;
			if (Projectile.ai[1] >= LifeAfterLandTicks)
				Projectile.Kill();
		}

		// Soft warm glow so the hazard reads at distance.
		Lighting.AddLight(Projectile.Center, 0.20f, 0.10f, 0.04f);

		// Sparse ember dust off the surface while live.
		if (Projectile.ai[0] == 1f && Main.rand.NextBool(8))
		{
			var d = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height,
				DustID.Smoke, 0f, -0.6f, 80, default, 1.0f);
			d.noGravity = true;
		}
	}

	public override bool OnTileCollide(Vector2 oldVelocity)
	{
		// Latch into stationary mode on first contact with terrain.
		if (Projectile.ai[0] == 0f)
		{
			Projectile.ai[0] = 1f;
			Projectile.ai[1] = 0f;
			Projectile.velocity = Vector2.Zero;
			Projectile.netUpdate = true;

			// Landing dust puff.
			if (!Main.dedServ)
			{
				for (int i = 0; i < 6; i++)
				{
					var d = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height,
						DustID.Smoke, 0f, -1f, 100, default, 1.2f);
					d.noGravity = true;
				}
			}
		}
		return false; // don't kill on tile contact
	}

	public override void ModifyHitPlayer(Player target, ref Player.HurtModifiers modifiers)
	{
		// Landed-block contact is graze damage (the player is touching coal, not
		// being hit by it). Falling-stage contact is full damage (chunk to the head).
		if (Projectile.ai[0] == 1f)
			modifiers.SourceDamage.Base -= ContactDamageReduction;
	}

	public override bool PreDraw(ref Color lightColor)
	{
		Texture2D tex = TextureAssets.Projectile[Type].Value;
		float fade = 1f;
		if (Projectile.ai[0] == 1f && Projectile.ai[1] > LifeAfterLandTicks - FadeTicks)
		{
			int remaining = LifeAfterLandTicks - (int)Projectile.ai[1];
			fade = MathHelper.Clamp(remaining / (float)FadeTicks, 0f, 1f);
		}
		Color tint = lightColor * fade;
		Vector2 pos = Projectile.Center - Main.screenPosition;
		var origin = tex.Size() * 0.5f;
		Main.spriteBatch.Draw(tex, pos, null, tint, Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0f);
		return false;
	}
}
