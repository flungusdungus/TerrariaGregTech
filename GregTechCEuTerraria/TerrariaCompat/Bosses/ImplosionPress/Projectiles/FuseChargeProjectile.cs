#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.ImplosionPress.Projectiles;

// One placed ITNT charge in the Fuse-Line Cascade attack. Boss spawns N of these
// in a horizontal line + a FuseFlame at one end; the flame travels left->right at
// fixed speed and "lights" each charge it passes (sets ai[0] = 1). Once lit, the
// charge plays a brief flash -> spawns a fast shockwave ring -> dies.
//
//   ai[0] = lit-state (0 = unlit waiting, 1 = lit fuse-burn, 2 = detonated)
//   ai[1] = ticks since lit (drives flash -> detonate timing)
public class FuseChargeProjectile : ModProjectile
{
	// ---- tunables ----------------------------------------------------------
	private const int FuseBurnTicks = 12; // ticks between lit and detonate
	private const int Lifetime = 60 * 30; // safety upper bound (30s); normally killed at detonation
	public const float OverlapRadius = 28f; // hitbox the FuseFlame uses to detect "I've reached this charge"

	public override string Texture => "GregTechCEuTerraria/Content/Textures/item/material_sets/ruby/gem";

	public override void SetDefaults()
	{
		Projectile.width = 24;
		Projectile.height = 24;
		Projectile.hostile = true;
		Projectile.friendly = false;
		Projectile.tileCollide = false;
		Projectile.penetrate = -1;
		Projectile.timeLeft = Lifetime;
		Projectile.aiStyle = -1;
		Projectile.ignoreWater = true;
	}

	// Unlit charges only graze (you can walk past one if you're careful);
	// lit charges don't damage either (the shockwave does that). Pure marker.
	public override bool? CanDamage() => false;

	public override void AI()
	{
		Projectile.velocity = Vector2.Zero;
		Lighting.AddLight(Projectile.Center, 0.30f, 0.10f, 0.05f);

		if (Projectile.ai[0] == 1f)
		{
			Projectile.ai[1]++;
			// Sparse sparks during burn.
			if (!Main.dedServ && Main.rand.NextBool(2))
			{
				var d = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height,
					DustID.Torch, 0f, -1f, 100, default, 1.4f);
				d.noGravity = true;
			}

			if (Projectile.ai[1] >= FuseBurnTicks && Main.netMode != NetmodeID.MultiplayerClient)
			{
				Detonate();
			}
		}
		else
		{
			// Unlit ambient: tiny red pulse so player sees the line.
			if (!Main.dedServ && Main.rand.NextBool(20))
			{
				var d = Dust.NewDustPerfect(Projectile.Center, DustID.RedTorch,
					Vector2.Zero, 100, default, 0.9f);
				d.noGravity = true;
			}
		}
	}

	private void Detonate()
	{
		Projectile.ai[0] = 2f;
		if (!Main.dedServ)
		{
			SoundEngine.PlaySound(SoundID.Item62 with { Volume = 0.9f }, Projectile.Center);
			for (int i = 0; i < 16; i++)
			{
				var d = Dust.NewDustDirect(Projectile.position - new Vector2(12, 12),
					48, 48, DustID.Torch, 0f, -1f, 100, default, 1.4f);
				d.noGravity = true;
			}
		}

		// Single fast shockwave ring at charge position.
		Projectile.NewProjectile(Projectile.GetSource_FromAI(), Projectile.Center, Vector2.Zero,
			ModContent.ProjectileType<PressureRingProjectile>(),
			Projectile.damage, 0f, Main.myPlayer);

		Projectile.Kill();
	}

	public override bool PreDraw(ref Color lightColor)
	{
		Texture2D tex = TextureAssets.Projectile[Type].Value;
		Vector2 pos = Projectile.Center - Main.screenPosition;
		var origin = tex.Size() * 0.5f;

		Color tint;
		float scale;
		if (Projectile.ai[0] == 0f)
		{
			// Unlit: deep red pulse.
			float pulse = 0.7f + 0.3f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 4f);
			tint = new Color(220, 80, 70) * pulse;
			scale = 1.0f;
		}
		else
		{
			// Lit: bright orange flicker.
			float t = MathHelper.Clamp(Projectile.ai[1] / (float)FuseBurnTicks, 0f, 1f);
			tint = Color.Lerp(new Color(255, 140, 60), new Color(255, 240, 200), t);
			scale = 1.0f + t * 0.4f;
		}
		Main.spriteBatch.Draw(tex, pos, null, tint, 0f, origin, scale, SpriteEffects.None, 0);
		return false;
	}
}
