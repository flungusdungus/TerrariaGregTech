#nullable enable
using System;
using GregTechCEuTerraria.Config;
using GregTechCEuTerraria.TerrariaCompat.Bosses.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.ImplosionPress.Projectiles;

// Heavy ITNT shell lobbed in a parabolic arc. On impact (tile collision OR
// max-flight-ticks), detonates into:
//   * a single fast Pressure Ring shockwave at impact site
//   * a Plasma-Fireball-style 8-way carbon ejecta spray
//
// Inspired by Calamity's AresGaussNukeProjectile (the arcing namesake) +
// AresPlasmaFireball (the on-death 8-way spray pattern).
//
//   ai[0] = age (drives reticle growth + spin)
//   ai[1] = palette flavour (0 = phase 1, 1 = phase 2)
public class MortarShellProjectile : ModProjectile
{
	// ---- tunables ---------------------------------------------------------
	private const int MaxFlightTicks = 200;
	private const float Gravity = 0.32f;
	private const float MaxFallSpeed = 18f;
	private const float ImpactReticleRadius = 110f;
	private const int EjectaCount = 8;
	private const float EjectaSpeed = 9f;
	private const int EjectaDamagePct = 70;
	private const float ImpactScreenshake = 8f;

	public override string Texture => "GregTechCEuTerraria/Content/Textures/item/material_sets/lignite/gem";

	public override void SetDefaults()
	{
		Projectile.width = 30;
		Projectile.height = 30;
		Projectile.hostile = true;
		Projectile.friendly = false;
		Projectile.tileCollide = true;
		Projectile.penetrate = -1;
		Projectile.timeLeft = MaxFlightTicks;
		Projectile.aiStyle = -1;
		Projectile.ignoreWater = true;
	}

	public override void AI()
	{
		Projectile.ai[0]++;
		Projectile.velocity.Y += Gravity;
		if (Projectile.velocity.Y > MaxFallSpeed) Projectile.velocity.Y = MaxFallSpeed;
		Projectile.rotation = (float)Math.Atan2(Projectile.velocity.Y, Projectile.velocity.X);

		// Sparse smoke trail.
		if (!Main.dedServ && Main.rand.NextBool(3))
		{
			var d = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height,
				DustID.Smoke, -Projectile.velocity.X * 0.1f, -Projectile.velocity.Y * 0.1f, 100, default, 1.4f);
			d.noGravity = true;
		}
		Lighting.AddLight(Projectile.Center, 0.30f, 0.12f, 0.05f);
	}

	public override bool? CanDamage() => null;

	public override bool OnTileCollide(Vector2 oldVelocity)
	{
		Detonate();
		return true;
	}

	public override void OnKill(int timeLeft)
	{
		// timeLeft expires -> also detonate (in case the shell flew offscreen).
		if (Projectile.timeLeft <= 0) Detonate();
	}

	private void Detonate()
	{
		if (!Main.dedServ)
		{
			SoundEngine.PlaySound(SoundID.Item62 with { Volume = 1.1f }, Projectile.Center);
			SoundEngine.PlaySound(SoundID.DD2_BetsyFireballImpact with { Volume = 0.8f, Pitch = -0.3f }, Projectile.Center);
			for (int i = 0; i < 26; i++)
			{
				var d = Dust.NewDustDirect(Projectile.position - new Vector2(20, 20),
					60, 60, DustID.Torch, 0f, -1f, 100, default, 1.6f);
				d.noGravity = true;
				d.velocity *= 1.5f;
			}
			Main.instance.CameraModifiers.Add(new Terraria.Graphics.CameraModifiers.PunchCameraModifier(
				Projectile.Center, Vector2.UnitY, ImpactScreenshake, 4f, 14, 2000f, FullName));
		}

		if (Main.netMode != NetmodeID.MultiplayerClient)
		{
			// Fast shockwave ring at impact site.
			Projectile.NewProjectile(Projectile.GetSource_FromAI(), Projectile.Center, Vector2.Zero,
				ModContent.ProjectileType<PressureRingProjectile>(),
				Projectile.damage * 6 / 10, 0f, Main.myPlayer);

			// 8-way ejecta spray.
			int palette = (int)Projectile.ai[1];
			int shardType = ModContent.ProjectileType<CarbonShardProjectile>();
			int shardDmg = Math.Max(1, Projectile.damage * EjectaDamagePct / 100);
			for (int i = 0; i < EjectaCount; i++)
			{
				float ang = MathHelper.TwoPi * i / EjectaCount;
				Vector2 vel = ang.ToRotationVector2() * EjectaSpeed;
				Projectile.NewProjectile(Projectile.GetSource_FromAI(),
					Projectile.Center, vel, shardType, shardDmg, 1.2f, Main.myPlayer,
					ai0: 1f, ai1: palette == 1 ? 2f : 1f /* dark-grey shrapnel */);
			}
		}
	}

	public override bool PreDraw(ref Color lightColor)
	{
		Texture2D tex = TextureAssets.Projectile[Type].Value;
		Vector2 pos = Projectile.Center - Main.screenPosition;
		var origin = tex.Size() * 0.5f;

		// Draw the shell itself.
		Color shellTint = new Color(60, 55, 50);
		Main.spriteBatch.Draw(tex, pos, null, shellTint, Projectile.rotation, origin, Projectile.scale * 1.4f, SpriteEffects.None, 0);

		// Draw a growing ground-reticle BELOW the shell (X-tracked, Y at first solid tile).
		Vector2 reticleAt = FindGroundBelow(Projectile.Center);
		float t = MathHelper.Clamp(Projectile.ai[0] / 90f, 0f, 1f);
		float r = ImpactReticleRadius * (0.4f + 0.6f * t);
		const int cloudType = ProjectileID.RainCloudRaining;
		Main.instance.LoadProjectile(cloudType);
		Texture2D cloud = TextureAssets.Projectile[cloudType].Value;
		int fh = cloud.Height / Math.Max(1, Main.projFrames[cloudType]);
		var src = new Rectangle(0, 0, cloud.Width, fh);
		Color ringC = Color.Lerp(new Color(255, 220, 120), new Color(255, 90, 60), t) * 0.6f;
		const int segs = 14;
		for (int i = 0; i < segs; i++)
		{
			float ang = MathHelper.TwoPi * i / segs;
			Vector2 at = reticleAt + ang.ToRotationVector2() * r - Main.screenPosition;
			Main.spriteBatch.Draw(cloud, at, src, ringC, ang, new Vector2(cloud.Width / 2f, fh / 2f), 0.6f, SpriteEffects.None, 0);
		}

		// Debug overlay: simulate the shell's remaining arc and draw a thin
		// parabola so you can verify the ground reticle actually matches where
		// it lands. Gated by GTConfig.DebugMobs. Steps every 4 ticks for ~80
		// ticks of look-ahead - cheap, ~20 line segments per shell.
		if (GTConfig.Instance.DebugMobs)
			DrawTrajectoryDebug(reticleAt);

		return false;
	}

	private void DrawTrajectoryDebug(Vector2 plannedImpact)
	{
		Vector2 pos = Projectile.Center;
		Vector2 vel = Projectile.velocity;
		Vector2 prevScreen = pos - Main.screenPosition;
		var c = new Color(255, 200, 100, 200);

		// Simulate forward in 4-tick chunks until we hit a solid tile or run out.
		for (int step = 0; step < 24; step++)
		{
			for (int s = 0; s < 4; s++) { vel.Y += Gravity; if (vel.Y > MaxFallSpeed) vel.Y = MaxFallSpeed; pos += vel; }
			Vector2 nextScreen = pos - Main.screenPosition;
			DebugOverlaySystem.DrawLine(
				Main.spriteBatch, prevScreen, nextScreen, c, 1);
			prevScreen = nextScreen;

			// Stop the prediction if we crossed the predicted ground line.
			if (pos.Y >= plannedImpact.Y) break;
		}
	}

	// Cast downward up to 40 tiles to find a solid ground tile beneath `from`.
	// If none found within range, fall back to from itself (reticle hangs in air).
	private static Vector2 FindGroundBelow(Vector2 from)
	{
		int x = (int)(from.X / 16f);
		int y0 = (int)(from.Y / 16f);
		for (int dy = 0; dy < 40; dy++)
		{
			int y = y0 + dy;
			if (y < 0 || y >= Main.maxTilesY) break;
			Tile tile = Main.tile[x, y];
			if (tile.HasTile && Main.tileSolid[tile.TileType])
				return new Vector2(x * 16f + 8f, y * 16f);
		}
		return from;
	}
}
