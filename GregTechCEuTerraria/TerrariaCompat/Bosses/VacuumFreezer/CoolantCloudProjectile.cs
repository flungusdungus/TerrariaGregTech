#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.VacuumFreezer;

// A lingering coolant fog the Vacuum Freezer vents into the arena. It deals no
// contact damage - its job is to CHILL (slow) any player standing in it, herding
// you out of safe space while shards fly and the vacuum pull tugs. The chill is
// applied on the owning client only (each client slows its own local player), so
// it's MP-correct without a packet, the same way the vanilla treasure-magnet
// pull runs client-local.
//
//   ai[0] = drift timer (counts up; drives the bob + draw frame)
public class CoolantCloudProjectile : ModProjectile
{
	private const int ChillRefresh = 30; // re-applied each overlap tick so it never lapses while inside

	public override string Texture => "GregTechCEuTerraria/Content/Textures/gui/icon/bucket_mode/water_drop";

	public override void SetDefaults()
	{
		Projectile.width = 110;
		Projectile.height = 70;
		Projectile.hostile = true;
		Projectile.friendly = false;
		Projectile.tileCollide = false;
		Projectile.penetrate = -1;
		Projectile.timeLeft = 330;
		Projectile.aiStyle = -1;
		Projectile.ignoreWater = true;
	}

	// The fog herds, it doesn't hurt - the chill + the shards are the threat.
	public override bool? CanDamage() => false;

	public override void AI()
	{
		// Slow drift + gentle vertical bob (drains horizontal momentum over time).
		Projectile.velocity.X *= 0.985f;
		Projectile.velocity.Y = (float)Math.Sin(Projectile.ai[0] * 0.07f) * 0.35f;
		Projectile.ai[0]++;

		// Frost-fog puff so the cloud reads as a billowing cold mass.
		if (!Main.dedServ && Main.rand.NextBool(2))
		{
			Vector2 at = Projectile.Center + new Vector2(Main.rand.Next(-54, 55), Main.rand.Next(-26, 27));
			var d = Dust.NewDustPerfect(at, DustID.Cloud,
				new Vector2(Main.rand.NextFloat(-0.25f, 0.25f), 0.15f), 120,
				new Color(200, 230, 250), Main.rand.NextFloat(1.5f, 2.4f));
			d.noGravity = true;
		}

		// Chill the local player while they're inside the fog. Owning-client-local
		// (Main.myPlayer); on a dedicated server there is no local player to slow.
		if (!Main.dedServ)
		{
			Player p = Main.player[Main.myPlayer];
			if (p.active && !p.dead && p.Hitbox.Intersects(Projectile.Hitbox))
				p.AddBuff(BuffID.Chilled, ChillRefresh);
		}
	}

	public override bool PreDraw(ref Color lightColor)
	{
		// Vanilla Nimbus rain-cloud sprite tinted icy-cyan - reads as a real cold
		// fog bank. The white cloud multiplies cleanly into the tint.
		const int cloudType = ProjectileID.RainCloudRaining;
		Main.instance.LoadProjectile(cloudType);
		Texture2D tex = TextureAssets.Projectile[cloudType].Value;
		int frames = Math.Max(1, Main.projFrames[cloudType]);
		int fh = tex.Height / frames;
		int frame = (int)(Projectile.ai[0] / 6f) % frames;
		var src = new Rectangle(0, frame * fh, tex.Width, fh);

		var frost = new Color(170, 215, 245);
		Color tint = new(
			(byte)(frost.R * lightColor.R / 255),
			(byte)(frost.G * lightColor.G / 255),
			(byte)(frost.B * lightColor.B / 255),
			(byte)(lightColor.A * 0.85f));
		float scale = Projectile.width * 1.5f / tex.Width;
		Vector2 pos = Projectile.Center - Main.screenPosition;
		Main.EntitySpriteDraw(tex, pos, src, tint, 0f, new Vector2(tex.Width / 2f, fh / 2f), scale, SpriteEffects.None, 0);
		return false;
	}
}
