#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.SoulDistiller;

// A belch of refinery off-gas - rises, expands, fades, and poisons on contact.
// The heavy tail segments vent these so the arena fills with drifting hazard
// clouds. Pure visual/contact hazard, no projectile spawning.
public class ToxicGasProjectile : ModProjectile
{
	private static readonly Color GasTint = new(150, 210, 90);

	public override string Texture => "GregTechCEuTerraria/Content/Textures/gui/icon/bucket_mode/water_drop";

	public override void SetDefaults()
	{
		Projectile.width = 46;
		Projectile.height = 46;
		Projectile.hostile = true;
		Projectile.friendly = false;
		Projectile.tileCollide = false;
		Projectile.penetrate = -1;
		Projectile.timeLeft = 180;
		Projectile.aiStyle = -1;
		Projectile.ignoreWater = true;
		Projectile.scale = 0.7f;
	}

	public override void AI()
	{
		// Rise and slow, expanding as it disperses; fade in then out.
		Projectile.velocity.Y -= 0.04f;
		if (Projectile.velocity.Y < -2.2f) Projectile.velocity.Y = -2.2f;
		Projectile.velocity.X *= 0.97f;
		Projectile.scale += 0.012f;

		int age = 180 - Projectile.timeLeft;
		Projectile.Opacity = age < 20 ? age / 20f : MathHelper.Clamp(Projectile.timeLeft / 60f, 0f, 1f);

		if (!Main.dedServ && Main.rand.NextBool(3))
		{
			var d = Dust.NewDustPerfect(Projectile.Center + Main.rand.NextVector2Circular(20, 20),
				DustID.GreenTorch, new Vector2(0f, -0.4f), 150, default, 1.0f);
			d.noGravity = true;
			d.velocity *= 0.2f;
		}
	}

	public override void OnHitPlayer(Player target, Player.HurtInfo info) => target.AddBuff(BuffID.Poisoned, 240);

	public override bool PreDraw(ref Color lightColor)
	{
		Texture2D tex = TextureAssets.Projectile[Type].Value;
		Color tint = SoulDistillerRenderer.Tint(lightColor, GasTint) * Projectile.Opacity;
		Vector2 pos = Projectile.Center - Main.screenPosition;
		Main.EntitySpriteDraw(tex, pos, null, tint, Projectile.rotation, tex.Size() * 0.5f, Projectile.scale * 3.2f, SpriteEffects.None, 0);
		return false;
	}
}
