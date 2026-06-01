#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.Graphics.CameraModifiers;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.ImplosionPress.Projectiles;

// Abstract base for expanding shockwave rings (Pressure Pulse, Detonator
// Shockwave, Diamond Forge). Parallels Calamity's BaseMassiveExplosionProjectile:
// a single hitbox-less projectile that grows from radius 0 to MaxRadius over
// Lifetime ticks, deals damage in a circular hitbox check against the player,
// flashes screen-shake on spawn, and draws as a translucent ring (cloud sprite).
// Subclasses set MaxRadius / Lifetime / Color / Screenshake.
//
// One-hit-per-player guard (PlayersHit) so a slow expanding ring doesn't multi-tick
// the same player.
//
//   ai[0] = age (drives expansion + draw + screenshake fade)
//   ai[1] = (free) - used by Diamond Forge to carry safe-vault angle
//   ai[2] = (free) - used by Diamond Forge to carry safe-vault radius
public abstract class MassiveExplosionRing : ModProjectile
{
	// ---- subclass tunables -------------------------------------------------
	protected abstract int Lifetime { get; }
	protected abstract float MaxRadius { get; }
	protected virtual float MinRadius => 0f;
	protected virtual float StartScreenshake => 12f;
	protected virtual int LightInterval => 2;
	protected virtual Color BaseColor => new(220, 130, 60);
	protected virtual Color FadeColor => new(255, 220, 180);
	// Per-tick easing of the radius: t=0 -> MinRadius, t=1 -> MaxRadius. Default
	// is a smoothstep (slow start + tail), subclasses can override for snappier.
	protected virtual float ExpansionEase(float t) => t * t * (3f - 2f * t);

	// One-hit-per-player so an expanding ring doesn't multi-tick the same target.
	private List<int> _hit = new() { -1 };

	public override string Texture => "GregTechCEuTerraria/Content/Textures/gui/icon/bucket_mode/water_drop";

	public override void SetStaticDefaults()
	{
		ProjectileID.Sets.DrawScreenCheckFluff[Type] = 4000;
	}

	public override void SetDefaults()
	{
		Projectile.width = 2;
		Projectile.height = 2;
		Projectile.hostile = true;
		Projectile.friendly = false;
		Projectile.ignoreWater = true;
		Projectile.tileCollide = false;
		Projectile.penetrate = -1;
		Projectile.timeLeft = Lifetime;
		Projectile.aiStyle = -1;
	}

	protected float CurrentRadius
	{
		get
		{
			float t = MathHelper.Clamp(Projectile.ai[0] / (float)Lifetime, 0f, 1f);
			return MathHelper.Lerp(MinRadius, MaxRadius, ExpansionEase(t));
		}
	}

	public override void AI()
	{
		Projectile.ai[0]++;

		// Screen-shake on spawn, fading out across the first ~1/3 of lifetime.
		if (Projectile.ai[0] == 1f && !Main.dedServ)
		{
			var mod = new PunchCameraModifier(Projectile.Center, Vector2.UnitY,
				StartScreenshake, 6f, 20, 2400f, FullName);
			Main.instance.CameraModifiers.Add(mod);
		}

		// Soft warm glow at the centre, decaying with age.
		if (LightInterval <= 1 || Main.GameUpdateCount % (uint)LightInterval == 0)
		{
			float k = 1f - (float)Projectile.ai[0] / Lifetime;
			Lighting.AddLight(Projectile.Center, 0.7f * k, 0.4f * k, 0.15f * k);
		}
	}

	public override bool CanHitPlayer(Player target)
	{
		if (_hit.Contains(target.whoAmI)) return false;
		float r = CurrentRadius;
		// Annular damage: only the ring face deals damage, not the empty centre.
		// Half-thickness 24 px = readable bullet-ring shape; outer at +24 px is
		// the visual edge.
		const float thickness = 36f;
		float d = Vector2.Distance(Projectile.Center, target.Center);
		return d <= r + thickness && d >= Math.Max(0f, r - thickness);
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

		float t = MathHelper.Clamp(Projectile.ai[0] / (float)Lifetime, 0f, 1f);
		float r = CurrentRadius;
		Color tint = Color.Lerp(BaseColor, FadeColor, t);
		tint *= 1f - t * 0.6f;

		// Draw the ring as ~16 cloud-puff segments around the circumference - cheap
		// and reads as a translucent shockwave.
		const int segs = 18;
		float scale = MathHelper.Lerp(0.5f, 1.7f, t);
		Vector2 origin = new(tex.Width / 2f, fh / 2f);
		for (int i = 0; i < segs; i++)
		{
			float ang = MathHelper.TwoPi * i / segs;
			Vector2 at = Projectile.Center + ang.ToRotationVector2() * r - Main.screenPosition;
			Main.spriteBatch.Draw(tex, at, src, tint, ang, origin, scale, SpriteEffects.None, 0);
		}
		return false;
	}
}

// 1. Pressure Pulse - single fast-expanding white ring. The "jump or duck" beat.
//    Fast lifetime + large radius = the player has one window to read it and
//    react. Inspired by Dungeon Guardian's skull-ring jump.
public class PressureRingProjectile : MassiveExplosionRing
{
	protected override int Lifetime => 38;
	protected override float MaxRadius => 560f;
	protected override float StartScreenshake => 10f;
	protected override Color BaseColor => new(255, 240, 220);
	protected override Color FadeColor => new(255, 200, 140);
	// Snap-out easing: most distance covered in the first half of lifetime so
	// it reads as a sudden shockwave, not a graceful bloom.
	protected override float ExpansionEase(float t) => 1f - (1f - t) * (1f - t);

	public override void OnSpawn(Terraria.DataStructures.IEntitySource src)
	{
		SoundEngine.PlaySound(SoundID.Item62 with { Pitch = -0.4f, Volume = 1.1f }, Projectile.Center);
	}
}

// 2. Detonator Shockwave - the boss-slam follow-up. THREE concentric rings
//    staggered by 8 ticks each so the player gets multi-beat dodging. Riff on
//    Calamity's AresGaussNukeProjectileBoom (3 expanding rings) + Holy Bomb pulse.
//    Spawn THREE of these in series (the boss spawner staggers spawn ticks); each
//    instance is a single ring.
public class DetonatorShockwaveProjectile : MassiveExplosionRing
{
	protected override int Lifetime => 52;
	protected override float MaxRadius => 460f;
	protected override float StartScreenshake => 16f;
	protected override Color BaseColor => new(245, 95, 75);   // ITNT red
	protected override Color FadeColor => new(255, 200, 110); // ember gold

	public override void OnSpawn(Terraria.DataStructures.IEntitySource src)
	{
		SoundEngine.PlaySound(SoundID.Item62 with { Volume = 1.2f }, Projectile.Center);
	}
}

// 3. Diamond Forge - the phase-2 signature one-shot. Single giant ring covering
//    the whole arena; player must reach a small safe vault outside its radius
//    OR (interpreted as inside its "eye") before it completes. We carry the safe
//    vault as an annular hole in CanHitPlayer:
//       ai[1] = vault world-X, ai[2] = vault world-Y (server-set)
//    A player inside VaultRadius of (ai[1], ai[2]) is immune. Renderer draws a
//    bright green ring at the vault so the player can find it.
public class DiamondForgeProjectile : MassiveExplosionRing
{
	private const float VaultRadius = 64f;

	protected override int Lifetime => 90;
	protected override float MaxRadius => 1600f;
	protected override float StartScreenshake => 24f;
	protected override Color BaseColor => new(255, 80, 60);   // hellfire
	protected override Color FadeColor => new(255, 255, 240); // white-hot

	public override void OnSpawn(Terraria.DataStructures.IEntitySource src)
	{
		// Stacked low-pitch boom: bomb + impact, audible across the arena.
		SoundEngine.PlaySound(SoundID.Item62 with { Pitch = -0.7f, Volume = 1.3f }, Projectile.Center);
		SoundEngine.PlaySound(SoundID.DD2_BetsyFireballImpact with { Volume = 1.2f }, Projectile.Center);
	}

	public override bool CanHitPlayer(Player target)
	{
		// Vault exemption: inside the safe ring, never hit.
		Vector2 vault = new(Projectile.ai[1], Projectile.ai[2]);
		if (Vector2.Distance(target.Center, vault) <= VaultRadius)
			return false;
		// Otherwise the ring face logic from the base.
		return base.CanHitPlayer(target);
	}

	public override bool PreDraw(ref Color lightColor)
	{
		// Draw the vault first as a bright green pulsing ring, then the main shockwave.
		const int cloudType = ProjectileID.RainCloudRaining;
		Main.instance.LoadProjectile(cloudType);
		Texture2D tex = TextureAssets.Projectile[cloudType].Value;
		int frames = Math.Max(1, Main.projFrames[cloudType]);
		int fh = tex.Height / frames;
		var src = new Rectangle(0, 0, tex.Width, fh);
		Vector2 origin = new(tex.Width / 2f, fh / 2f);

		Vector2 vault = new(Projectile.ai[1], Projectile.ai[2]);
		float pulse = 0.85f + 0.15f * (float)Math.Sin(Projectile.ai[0] * 0.3f);
		Color vaultC = new Color(110, 245, 110) * pulse;
		const int vaultSegs = 14;
		for (int i = 0; i < vaultSegs; i++)
		{
			float ang = MathHelper.TwoPi * i / vaultSegs;
			Vector2 at = vault + ang.ToRotationVector2() * VaultRadius - Main.screenPosition;
			Main.spriteBatch.Draw(tex, at, src, vaultC, ang, origin, 0.9f, SpriteEffects.None, 0);
		}

		return base.PreDraw(ref lightColor);
	}
}
