#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.CausticReactor;

// A lingering pool of corrosive sludge the Caustic Reactor leaves on the ground
// during the Cardioid finale (the ONLY spell with terrain hazards). Unlike the
// bullet patterns it DOES damage on contact - it's a small area-denial puddle
// that punishes standing still in the safe lanes. Hovers in place, bobbing
// slightly, venting acid bubbles. The contact damage is applied via the normal
// hostile-projectile path (server-authoritative), so no client-local hack.
//
//   ai[0] = age timer (drives bob + bubble cadence + fade-out)
public class CorrosivePoolProjectile : ModProjectile
{
	// Tunables.
	private const int LifeTicks = 360;
	private const int FadeTicks = 60; // fade the hitbox + draw out over the last N ticks

	public override string Texture => "GregTechCEuTerraria/Content/Textures/gui/icon/bucket_mode/water_drop";

	public override void SetDefaults()
	{
		Projectile.width = 90;
		Projectile.height = 36;
		Projectile.hostile = true;
		Projectile.friendly = false;
		Projectile.tileCollide = false;
		Projectile.penetrate = -1;
		Projectile.timeLeft = LifeTicks;
		Projectile.aiStyle = -1;
		Projectile.ignoreWater = true;
	}

	// Stop hurting once it starts fading, so the visual fade reads as "safe now".
	public override bool? CanDamage() => Projectile.ai[0] < LifeTicks - FadeTicks ? null : false;

	public override void AI()
	{
		Projectile.ai[0]++;
		Projectile.velocity *= 0.9f;

		if (!Main.dedServ && Main.rand.NextBool(2))
		{
			Vector2 at = Projectile.Center + new Vector2(Main.rand.Next(-44, 45), Main.rand.Next(-12, 8));
			var d = Dust.NewDustPerfect(at, DustID.GreenFairy,
				new Vector2(Main.rand.NextFloat(-0.3f, 0.3f), -Main.rand.NextFloat(0.3f, 1.0f)), 100,
				default, Main.rand.NextFloat(1.0f, 1.8f));
			d.noGravity = true;
		}

		Lighting.AddLight(Projectile.Center, 0.18f, 0.32f, 0.08f);
	}

	public override bool PreDraw(ref Color lightColor)
	{
		const int cloudType = ProjectileID.RainCloudRaining;
		Main.instance.LoadProjectile(cloudType);
		Texture2D tex = TextureAssets.Projectile[cloudType].Value;
		int frames = Math.Max(1, Main.projFrames[cloudType]);
		int fh = tex.Height / frames;
		int frame = (int)(Projectile.ai[0] / 7f) % frames;
		var src = new Rectangle(0, frame * fh, tex.Width, fh);

		float fade = 1f;
		int remaining = LifeTicks - (int)Projectile.ai[0];
		if (remaining < FadeTicks) fade = MathHelper.Clamp(remaining / (float)FadeTicks, 0f, 1f);

		var acid = new Color(140, 200, 70);
		Color tint = new(
			(byte)(acid.R * lightColor.R / 255),
			(byte)(acid.G * lightColor.G / 255),
			(byte)(acid.B * lightColor.B / 255),
			(byte)(lightColor.A * 0.85f * fade));
		float scale = Projectile.width * 1.4f / tex.Width;
		Vector2 pos = Projectile.Center - Main.screenPosition;
		Main.EntitySpriteDraw(tex, pos, src, tint, 0f, new Vector2(tex.Width / 2f, fh / 2f), scale, SpriteEffects.None, 0);
		return false;
	}
}
