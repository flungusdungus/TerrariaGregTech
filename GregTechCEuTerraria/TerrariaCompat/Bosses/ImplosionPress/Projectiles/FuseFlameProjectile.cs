#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.ImplosionPress.Projectiles;

// Traveling fuse spark for the Fuse-Line Cascade. Moves at FuseSpeed in its
// initial velocity direction (set by boss to point along the line). On overlap
// with any FuseChargeProjectile, lights that charge (sets its ai[0] = 1) and
// continues. Dies after MaxLifeTicks or when it exits the arena bounds.
//
// Pure visual + scanner - does no damage itself. Player threat is the chain of
// charge detonations behind it, not the spark.
public class FuseFlameProjectile : ModProjectile
{
	// ---- tunables ----------------------------------------------------------
	private const int MaxLifeTicks = 240;
	public const float FuseSpeed = 4.5f; // pixels per tick; spawner velocity should equal this magnitude

	public override string Texture => "GregTechCEuTerraria/Content/Textures/item/material_sets/ruby/gem";

	public override void SetDefaults()
	{
		Projectile.width = 18;
		Projectile.height = 18;
		Projectile.hostile = false;
		Projectile.friendly = false;
		Projectile.tileCollide = false;
		Projectile.penetrate = -1;
		Projectile.timeLeft = MaxLifeTicks;
		Projectile.aiStyle = -1;
		Projectile.ignoreWater = true;
	}

	public override bool? CanDamage() => false;

	public override void AI()
	{
		// Constant-speed traveler. Normalize once if velocity was set with a
		// different magnitude than FuseSpeed.
		if (Projectile.velocity.Length() > 0.01f)
			Projectile.velocity = Projectile.velocity.SafeNormalize(Vector2.UnitX) * FuseSpeed;

		// Light + sparks.
		Lighting.AddLight(Projectile.Center, 0.6f, 0.35f, 0.10f);
		if (!Main.dedServ)
		{
			for (int i = 0; i < 2; i++)
			{
				var d = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height,
					DustID.Torch, 0f, -1f, 100, default, 1.2f);
				d.noGravity = true;
				d.velocity *= 0.6f;
			}
		}

		// Scan for adjacent unlit fuse charges. Server-side: only the host
		// authoritatively flips ai[0] = 1, then netUpdate syncs the change.
		if (Main.netMode == NetmodeID.MultiplayerClient) return;

		int chargeType = ModContent.ProjectileType<FuseChargeProjectile>();
		for (int i = 0; i < Main.maxProjectiles; i++)
		{
			Projectile p = Main.projectile[i];
			if (!p.active || p.type != chargeType || p.ai[0] != 0f) continue;
			if (Vector2.Distance(p.Center, Projectile.Center) <= FuseChargeProjectile.OverlapRadius)
			{
				p.ai[0] = 1f;
				p.ai[1] = 0f;
				p.netUpdate = true;
			}
		}
	}

	public override bool PreDraw(ref Color lightColor)
	{
		Texture2D tex = TextureAssets.Projectile[Type].Value;
		Vector2 pos = Projectile.Center - Main.screenPosition;
		var origin = tex.Size() * 0.5f;
		float pulse = 0.85f + 0.15f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 12f);
		Color tint = new Color(255, 200, 80) * pulse;
		Main.spriteBatch.Draw(tex, pos, null, tint, 0f, origin, 1.1f, SpriteEffects.None, 0);
		return false;
	}
}
