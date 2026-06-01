#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.ImplosionPress.Projectiles;

// The Implosion Press's signature ground hazard: a circular telegraph that the
// boss designates near the player, ticks down through visible color stages, then
// IMPLODES (brief inward suck) and DETONATES (ring shockwave + carbon ejecta).
//
// Stages (driven by ai[0] tick counter; each stage ends at its end-tick):
//
//   [OutlineEnd]   white outline drawn      -> "marked, no damage"
//   [WarmEnd]      orange fill              -> "primed, getting closer"
//   [StrobeEnd]    red strobe + chirp       -> "imminent"
//   [SuckEnd]      INWARD pull on player    -> "you're being yanked"
//   [DetonateEnd]  damage tick + spawn ring + 8-way carbon ejecta
//   [dies one tick later]
//
// Pull semantics mirror Vacuum Freezer: applied client-local to Main.LocalPlayer
// only, no packet (vanilla treasure-magnet pattern - MP-correct because each
// client computes its own pull from synced projectile position).
//
//   ai[0] = age in ticks
//   ai[1] = palette flavour (0 = phase 1 / standard, 1 = phase 2 / Diamond Forge palette)
public class CrushZoneProjectile : ModProjectile
{
	// ---- tunables (read by the spawn code + the renderer) ------------------
	public const int OutlineEnd  = 60;   // 1.0s
	public const int WarmEnd     = 130;  // +1.2s
	public const int StrobeEnd   = 180;  // +0.8s
	public const int SuckEnd     = 200;  // +0.3s (the implosion suck)
	public const int DetonateEnd = 205;  // +1 damage tick frame after suck
	public const int Lifetime    = 220;  // tail for fade

	public const float Radius = 96f;        // damage + visual radius
	public const float SuckMaxPull = 7.5f;  // px/tick velocity towards centre at radius edge
	public const int EjectaCount = 8;
	public const float EjectaSpeed = 9f;
	public const int EjectaDamagePct = 75;  // % of the zone's damage carried by each shard

	// Used so the zone damages each player at most once (the detonate frame
	// is a single hostile tick, but CanHitPlayer would return true on subsequent
	// frames if Lifetime > DetonateEnd).
	private List<int> _hit = new() { -1 };

	public override string Texture => "GregTechCEuTerraria/Content/Textures/gui/icon/bucket_mode/water_drop";

	public override void SetStaticDefaults()
	{
		ProjectileID.Sets.DrawScreenCheckFluff[Type] = 1500;
	}

	public override void SetDefaults()
	{
		Projectile.width = 2;
		Projectile.height = 2;
		Projectile.hostile = true;
		Projectile.friendly = false;
		Projectile.tileCollide = false;
		Projectile.penetrate = -1;
		Projectile.timeLeft = Lifetime;
		Projectile.aiStyle = -1;
		Projectile.ignoreWater = true;
	}

	public override void AI()
	{
		Projectile.ai[0]++;
		int t = (int)Projectile.ai[0];

		// Stage-specific behaviour.
		if (t == WarmEnd && !Main.dedServ)
			SoundEngine.PlaySound(SoundID.Item62 with { Pitch = 0.3f, Volume = 0.6f }, Projectile.Center);
		if (t == StrobeEnd && !Main.dedServ)
			SoundEngine.PlaySound(SoundID.Item62 with { Pitch = 0.7f, Volume = 0.7f }, Projectile.Center);

		// Suck stage: pull the local player inward, scaled by remaining distance.
		// Strength ramps over the brief SuckEnd-StrobeEnd window. Client-local
		// (treasure-magnet pattern) - safe in MP, every client computes its own.
		if (t > StrobeEnd && t <= SuckEnd)
		{
			Player p = Main.LocalPlayer;
			if (p != null && p.active && !p.dead)
			{
				float dist = Vector2.Distance(p.Center, Projectile.Center);
				if (dist <= Radius)
				{
					float k = 1f - dist / Radius; // 1 at centre, 0 at edge
					Vector2 dir = (Projectile.Center - p.Center).SafeNormalize(Vector2.Zero);
					p.velocity += dir * SuckMaxPull * k * 0.5f; // accumulate over the few suck ticks
				}
			}

			// Inward dust streamers along the radius - reads as "being pulled in".
			if (!Main.dedServ && Main.rand.NextBool(2))
			{
				float a = Main.rand.NextFloat(MathHelper.TwoPi);
				Vector2 at = Projectile.Center + a.ToRotationVector2() * Radius;
				var d = Dust.NewDustPerfect(at, DustID.Smoke,
					(Projectile.Center - at) * 0.08f, 100, default, 1.4f);
				d.noGravity = true;
			}
		}

		// Detonate tick: spawn shockwave + 8-way ejecta + sound + screen-shake-via-projectile.
		if (t == DetonateEnd)
		{
			if (!Main.dedServ)
			{
				SoundEngine.PlaySound(SoundID.Item62 with { Volume = 1.0f }, Projectile.Center);
				SoundEngine.PlaySound(SoundID.DD2_KoboldExplosion with { Volume = 0.9f }, Projectile.Center);
				for (int i = 0; i < 22; i++)
				{
					var d = Dust.NewDustDirect(Projectile.position - new Vector2(Radius, Radius),
						(int)Radius * 2, (int)Radius * 2, DustID.Torch, 0f, -1f, 100, default, 1.6f);
					d.noGravity = true;
				}
			}

			if (Main.netMode != NetmodeID.MultiplayerClient)
			{
				// Outward shockwave ring (single fast pulse, NOT the full Detonator
				// triple - this is a Crush Zone, the shockwave is its closing punch).
				Projectile.NewProjectile(Projectile.GetSource_FromAI(),
					Projectile.Center, Vector2.Zero,
					ModContent.ProjectileType<PressureRingProjectile>(),
					Projectile.damage / 2, 0f, Main.myPlayer);

				// 8-way carbon ejecta. Palette per zone flavour.
				int palette = (int)Projectile.ai[1];
				int shardType = ModContent.ProjectileType<CarbonShardProjectile>();
				int shardDmg = Math.Max(1, Projectile.damage * EjectaDamagePct / 100);
				for (int i = 0; i < EjectaCount; i++)
				{
					float ang = MathHelper.TwoPi * i / EjectaCount;
					Vector2 vel = ang.ToRotationVector2() * EjectaSpeed;
					Projectile.NewProjectile(Projectile.GetSource_FromAI(),
						Projectile.Center, vel, shardType, shardDmg, 1.2f, Main.myPlayer,
						ai0: 1f /* arc-gravity ON */, ai1: palette == 1 ? 2f : 0f);
				}
			}
		}

		// Ambient warm glow grows with the strobe stage.
		float glowK = t <= OutlineEnd ? 0.2f
		           : t <= WarmEnd ? 0.45f
		           : t <= StrobeEnd ? 0.85f
		           : t <= SuckEnd ? 1.2f
		           : t <= DetonateEnd ? 1.5f
		           : 0.6f * Math.Max(0f, (Lifetime - t) / 16f);
		Lighting.AddLight(Projectile.Center, 0.6f * glowK, 0.30f * glowK, 0.10f * glowK);
	}

	public override bool? CanDamage()
	{
		int t = (int)Projectile.ai[0];
		return t == DetonateEnd ? null : false;
	}

	public override bool CanHitPlayer(Player target)
	{
		if (_hit.Contains(target.whoAmI)) return false;
		float d = Vector2.Distance(Projectile.Center, target.Center);
		if (d > Radius) return false;
		return true;
	}

	public override void OnHitPlayer(Player target, Player.HurtInfo info) => _hit.Add(target.whoAmI);

	public override bool PreDraw(ref Color lightColor)
	{
		const int cloudType = ProjectileID.RainCloudRaining;
		Main.instance.LoadProjectile(cloudType);
		Texture2D tex = TextureAssets.Projectile[cloudType].Value;
		int frames = Math.Max(1, Main.projFrames[cloudType]);
		int fh = tex.Height / frames;
		var src = new Rectangle(0, 0, tex.Width, fh);
		Vector2 origin = new(tex.Width / 2f, fh / 2f);

		int t = (int)Projectile.ai[0];

		// Choose tint per stage. Phase-2 (ai[1]==1) tints are slightly hotter.
		bool phase2 = Projectile.ai[1] >= 1f;
		Color tint;
		float fillOpacity;
		if (t <= OutlineEnd)            { tint = Color.White;                                fillOpacity = 0.10f; }
		else if (t <= WarmEnd)          { tint = new Color(255, 170, 80);                    fillOpacity = 0.28f; }
		else if (t <= StrobeEnd)        { // strobing red-orange
			float strobe = 0.55f + 0.45f * (float)Math.Sin(t * 0.65f);
			tint = Color.Lerp(new Color(255, 110, 80), new Color(255, 220, 180), strobe);
			fillOpacity = 0.45f;
		}
		else if (t <= SuckEnd)          { tint = phase2 ? new Color(255, 230, 230) : new Color(255, 200, 160); fillOpacity = 0.75f; }
		else if (t <= DetonateEnd)      { tint = Color.White;                                fillOpacity = 0.95f; }
		else { // fade out
			float k = MathHelper.Clamp((Lifetime - t) / 16f, 0f, 1f);
			tint = new Color(255, 200, 140);
			fillOpacity = 0.5f * k;
		}

		// Inner fill - a stack of cloud puffs at the centre + a sparse outer ring
		// to mark the boundary.
		Vector2 c = Projectile.Center - Main.screenPosition;
		float scaleInner = Radius / tex.Width * 2.6f;
		Main.spriteBatch.Draw(tex, c, src, tint * fillOpacity, 0f, origin, scaleInner, SpriteEffects.None, 0);

		// Outer ring - 12 puffs at the radius
		const int segs = 12;
		Color ringC = tint * Math.Min(1f, fillOpacity * 1.4f);
		for (int i = 0; i < segs; i++)
		{
			float ang = MathHelper.TwoPi * i / segs;
			Vector2 at = Projectile.Center + ang.ToRotationVector2() * Radius - Main.screenPosition;
			Main.spriteBatch.Draw(tex, at, src, ringC, ang, origin, 0.6f, SpriteEffects.None, 0);
		}

		return false;
	}
}
