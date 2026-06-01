#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.SoulDistiller;

// A hovering oil cloud that drips burning rain - the heavy-oil fraction's
// signature, modelled on the Nimbus Rod cloud (drifts, then spawns falling
// damage projectiles below itself on a timer). The cloud body deals no contact
// damage; the rain does. Server spawns the rain; clients just draw + puff.
//
//   ai[0] = fraction index (tint + which rain colour)
//   ai[1] = drip timer (counts up; reset each drop)
public class OilCloudProjectile : ModProjectile
{
	private const int DripEvery = 16;
	private const int RainDamage = 26;

	public override string Texture => "GregTechCEuTerraria/Content/Textures/gui/icon/bucket_mode/water_drop";

	public override void SetDefaults()
	{
		Projectile.width = 90;
		Projectile.height = 50;
		Projectile.hostile = true;
		Projectile.friendly = false;
		Projectile.tileCollide = false;
		Projectile.penetrate = -1;
		Projectile.timeLeft = 360;
		Projectile.aiStyle = -1;
		Projectile.ignoreWater = true;
	}

	// A cloud shouldn't punish you for touching it - the rain is the threat.
	public override bool? CanDamage() => false;

	public override void AI()
	{
		// Slow drift + gentle vertical bob.
		Projectile.velocity.X *= 0.98f;
		Projectile.velocity.Y = (float)System.Math.Sin(Projectile.ai[1] * 0.08f) * 0.4f;

		// Dark oily puff so the cloud reads as a billowing mass, not a flat sprite.
		if (!Main.dedServ && Main.rand.NextBool(2))
		{
			Vector2 at = Projectile.Center + new Vector2(Main.rand.Next(-44, 45), Main.rand.Next(-18, 19));
			var d = Dust.NewDustPerfect(at, DustID.Smoke, new Vector2(Main.rand.NextFloat(-0.3f, 0.3f), 0.2f), 120, default, Main.rand.NextFloat(1.4f, 2.2f));
			d.noGravity = true;
		}

		Projectile.ai[1]++;
		if (Projectile.ai[1] % DripEvery == 0 && Main.netMode != NetmodeID.MultiplayerClient)
		{
			Vector2 pos = Projectile.Center + new Vector2(Main.rand.Next(-40, 41), 18f);
			Projectile.NewProjectile(Projectile.GetSource_FromAI(), pos,
				new Vector2(Main.rand.NextFloat(-0.6f, 0.6f), Main.rand.NextFloat(5f, 8f)),
				ModContent.ProjectileType<LiquidGlobProjectile>(), RainDamage, 1f, Main.myPlayer,
				ai0: 1f, ai1: Projectile.ai[0]);
		}
	}

	public override bool PreDraw(ref Color lightColor)
	{
		// Draw the vanilla Nimbus rain-cloud sprite tinted to the fraction colour -
		// it reads as a real (oily) storm cloud. The white cloud multiplies cleanly
		// into any tint (the red BloodCloud would muddy it).
		const int cloudType = ProjectileID.RainCloudRaining;
		Main.instance.LoadProjectile(cloudType);
		Texture2D tex = TextureAssets.Projectile[cloudType].Value;
		int frames = System.Math.Max(1, Main.projFrames[cloudType]);
		int fh = tex.Height / frames;
		int frame = (int)(Projectile.ai[1] / 6f) % frames;
		var src = new Rectangle(0, frame * fh, tex.Width, fh);

		Color frac = SoulDistillerRenderer.Fractions[((int)Projectile.ai[0] % SoulDistillerRenderer.FractionCount + SoulDistillerRenderer.FractionCount) % SoulDistillerRenderer.FractionCount];
		Color tint = SoulDistillerRenderer.Tint(lightColor, frac);
		float scale = Projectile.width * 1.6f / tex.Width; // size the cloud to the hitbox + overhang
		Vector2 pos = Projectile.Center - Main.screenPosition;
		Main.EntitySpriteDraw(tex, pos, src, tint, 0f, new Vector2(tex.Width / 2f, fh / 2f), scale, SpriteEffects.None, 0);
		return false;
	}
}
