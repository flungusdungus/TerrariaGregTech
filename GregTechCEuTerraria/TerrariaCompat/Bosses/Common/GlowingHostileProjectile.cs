#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.Common;

// Reusable hostile boss projectile: a glowing sprite tinted to a random palette
// entry, optionally arcing under gravity, trailing dust + light, applying a
// debuff on hit, drawn as a self-lit base (+ optional bright overlay).
//
// Spawner sets:  ai[0] == 1 -> arc under gravity (else straight);  ai[1] -> palette index.
// Subclass sets: Texture (base sprite) + Palette; optionally overrides the
// overlay path, debuff, glow colour, gravity, spin, trail dust.
public abstract class GlowingHostileProjectile : ModProjectile
{
	protected abstract Color[] Palette { get; }
	protected virtual string? OverlayTexturePath => null;
	protected virtual int HitDebuff => BuffID.OnFire;
	protected virtual int HitDebuffDuration => 180;
	protected virtual Vector3 GlowColor => new(0.85f, 0.42f, 0.12f);
	protected virtual float ArcGravity => 0.14f;
	protected virtual float MaxFallSpeed => 13f;
	protected virtual float SpinSpeed => 0.30f;
	protected virtual int TrailDustType => DustID.Torch;

	// Bullet-hell throttles. A dense pattern (hundreds of live bullets) can't
	// afford a dust spawn + light add per bullet per tick - dust thrashes the
	// 6000-cap pool and the GC churns. Subclasses used in volume (AcidDroplet)
	// raise TrailDustChance (1-in-N; 0 = no trail) and LightInterval (add light
	// every N ticks, staggered per-bullet). Defaults preserve the original
	// every-other-tick dust + every-tick light for the sparse bosses (FrostShard,
	// HotIngot) so their feel is unchanged.
	protected virtual int TrailDustChance => 2; // Main.rand.NextBool(N); 0 disables
	protected virtual int LightInterval => 1;   // Lighting.AddLight every N ticks

	private Asset<Texture2D>? _overlay;

	public override void SetDefaults()
	{
		Projectile.width = 40;
		Projectile.height = 40;
		Projectile.scale = 3f;
		Projectile.hostile = true;
		Projectile.friendly = false;
		// Default ON: arcing projectiles (SupercooledIngot, HotIngot) rely on
		// dying-on-tile -> OnKill to shatter/splat on landing. Pure bullet-hell
		// spray bullets (AcidDroplet, FrostShard) override this to false so they
		// pass through terrain and keep geometric patterns intact.
		Projectile.tileCollide = true;
		Projectile.penetrate = 1;
		Projectile.timeLeft = 300;
		Projectile.aiStyle = -1;
		Projectile.ignoreWater = true;
	}

	public override void AI()
	{
		float dir = Math.Sign(Projectile.velocity.X);
		if (dir == 0f) dir = 1f;
		Projectile.rotation += SpinSpeed * dir;

		if (Projectile.ai[0] == 1f)
		{
			Projectile.velocity.Y += ArcGravity;
			if (Projectile.velocity.Y > MaxFallSpeed) Projectile.velocity.Y = MaxFallSpeed;
		}

		// Stagger the light add across bullets (identity offset) so a dense volley
		// doesn't queue hundreds of AddLight calls on the same frame.
		if (LightInterval <= 1 || (Main.GameUpdateCount + (uint)Projectile.identity) % (uint)LightInterval == 0)
			Lighting.AddLight(Projectile.Center, GlowColor.X, GlowColor.Y, GlowColor.Z);

		if (TrailDustChance > 0 && Main.rand.NextBool(TrailDustChance))
		{
			var d = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height,
				TrailDustType, 0f, 0f, 100, default, 1f);
			d.noGravity = true;
			d.velocity *= 0.3f;
		}
	}

	public override void OnHitPlayer(Player target, Player.HurtInfo info)
	{
		if (HitDebuff >= 0) target.AddBuff(HitDebuff, HitDebuffDuration);
	}

	public override void OnKill(int timeLeft)
	{
		// No death SOUND - these spawn in volume (a bullet-hell card can have
		// hundreds expiring at once, and a per-bullet sound is grating spam).
		// A small silent dust poof is fine.
		for (int i = 0; i < 4; i++)
		{
			var d = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height,
				TrailDustType, 0f, 0f, 100, default, 1.1f);
			d.noGravity = true;
		}
	}

	private Color Tint
	{
		get
		{
			var p = Palette;
			return p.Length == 0 ? Color.White : p[((int)Projectile.ai[1] % p.Length + p.Length) % p.Length];
		}
	}

	public override bool PreDraw(ref Color lightColor)
	{
		var tex = TextureAssets.Projectile[Type].Value;
		Vector2 pos = Projectile.Center - Main.screenPosition;
		var origin = tex.Size() * 0.5f;

		// Base sprite tinted to its glowing palette colour x ambient light.
		Color baseC = new(
			(byte)(Tint.R * lightColor.R / 255),
			(byte)(Tint.G * lightColor.G / 255),
			(byte)(Tint.B * lightColor.B / 255),
			lightColor.A);
		Main.EntitySpriteDraw(tex, pos, null, baseC, Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0);

		// Bright molten overlay (full white = self-lit).
		if (OverlayTexturePath is string path)
		{
			_overlay ??= ModContent.Request<Texture2D>(path);
			if (_overlay?.Value is Texture2D ov)
				Main.EntitySpriteDraw(ov, pos, null, Color.White, Projectile.rotation, ov.Size() * 0.5f, Projectile.scale, SpriteEffects.None, 0);
		}
		return false;
	}
}
