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

// "Implosion Tether" attack helper. The boss spawns this with:
//   ai[0] = (age, runs in AI)
//   ai[1] = marker world-X (player position at spawn)
//   ai[2] = marker world-Y
//
// Visual: a thin red beam drawn from the boss -> marker for SustainTicks; the
// marker draws a pulsing reticle. Damage = ZERO until expiry. On expiry, spawns
// a CrushZone at the marker and dies. Player's job is to NOT be at the marker
// position at lock-in time.
//
// Caller stores boss whoAmI via Projectile.owner is not appropriate (owner is
// always player). We resolve the boss each tick by scanning NPCs for the
// ImplosionPress type - cheap (1 active boss at a time).
public class ImplosionTetherProjectile : ModProjectile
{
	// ---- tunables ---------------------------------------------------------
	public const int SustainTicks = 180; // 3.0s lock-in window
	public const int Lifetime = 200;

	public override string Texture => "GregTechCEuTerraria/Content/Textures/gui/icon/bucket_mode/water_drop";

	public override void SetDefaults()
	{
		Projectile.width = 2;
		Projectile.height = 2;
		Projectile.hostile = false;     // beam is harmless; CrushZone does the damage
		Projectile.friendly = false;
		Projectile.tileCollide = false;
		Projectile.penetrate = -1;
		Projectile.timeLeft = Lifetime;
		Projectile.aiStyle = -1;
		Projectile.ignoreWater = true;
	}

	public override bool? CanDamage() => false;

	public override void AI()
	{
		Projectile.ai[0]++;
		int t = (int)Projectile.ai[0];

		// Marker glow.
		Vector2 marker = new(Projectile.ai[1], Projectile.ai[2]);
		Lighting.AddLight(marker, 0.7f, 0.25f, 0.20f);

		// Soft chirp every 30 ticks during sustain - audible countdown.
		if (t > 0 && t % 30 == 0 && !Main.dedServ)
			SoundEngine.PlaySound(SoundID.Item122 with { Volume = 0.5f, Pitch = -0.4f + t / (float)SustainTicks }, marker);

		if (t == SustainTicks)
		{
			if (Main.netMode != NetmodeID.MultiplayerClient)
			{
				// Phase flavour: read live from the boss at detonate time (the
				// tether projectile has no free ai slot - Projectile.ai is float[3]).
				bool phase2 = ResolveBossPhase2();
				Projectile.NewProjectile(Projectile.GetSource_FromAI(), marker, Vector2.Zero,
					ModContent.ProjectileType<CrushZoneProjectile>(),
					Projectile.damage, 1f, Main.myPlayer,
					ai0: CrushZoneProjectile.StrobeEnd /* skip telegraph - this WAS the telegraph */,
					ai1: phase2 ? 1f : 0f);
			}
			Projectile.Kill();
		}
	}

	private static bool ResolveBossPhase2()
	{
		int bossType = ModContent.NPCType<ImplosionPress>();
		for (int i = 0; i < Main.maxNPCs; i++)
		{
			var npc = Main.npc[i];
			if (npc.active && npc.type == bossType)
				return npc.ai[3] >= 1f;
		}
		return false;
	}

	public override bool PreDraw(ref Color lightColor)
	{
		// Find the boss to draw beam from boss -> marker.
		Vector2 from = Projectile.Center; // fallback
		int bossType = ModContent.NPCType<ImplosionPress>();
		for (int i = 0; i < Main.maxNPCs; i++)
		{
			var npc = Main.npc[i];
			if (npc.active && npc.type == bossType) { from = npc.Center; break; }
		}
		Vector2 marker = new(Projectile.ai[1], Projectile.ai[2]);
		int t = (int)Projectile.ai[0];
		float k = MathHelper.Clamp(t / (float)SustainTicks, 0f, 1f);

		// Beam: stack cloud puffs along the line, fading thickness.
		const int cloudType = ProjectileID.RainCloudRaining;
		Main.instance.LoadProjectile(cloudType);
		Texture2D cloud = TextureAssets.Projectile[cloudType].Value;
		int fh = cloud.Height / Math.Max(1, Main.projFrames[cloudType]);
		var src = new Rectangle(0, 0, cloud.Width, fh);
		Vector2 origin = new(cloud.Width / 2f, fh / 2f);

		Vector2 diff = marker - from;
		float len = diff.Length();
		if (len > 1f)
		{
			Vector2 step = diff / len;
			const float spacing = 22f;
			int n = (int)(len / spacing);
			Color beamC = Color.Lerp(new Color(255, 130, 80), new Color(255, 60, 50), k) * 0.55f;
			for (int i = 0; i < n; i++)
			{
				Vector2 at = from + step * (i * spacing) - Main.screenPosition;
				Main.spriteBatch.Draw(cloud, at, src, beamC, 0f, origin, 0.45f, SpriteEffects.None, 0);
			}
		}

		// Marker reticle - tightens as sustain progresses.
		float r = MathHelper.Lerp(80f, 40f, k);
		float pulse = 0.7f + 0.3f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 12f);
		Color markerC = new Color(255, 80, 60) * pulse;
		const int segs = 14;
		for (int i = 0; i < segs; i++)
		{
			float ang = MathHelper.TwoPi * i / segs;
			Vector2 at = marker + ang.ToRotationVector2() * r - Main.screenPosition;
			Main.spriteBatch.Draw(cloud, at, src, markerC, ang, origin, 0.55f, SpriteEffects.None, 0);
		}
		return false;
	}
}
