#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.SoulDistiller;

// A discarded empty bucket flung from the column - heavy junk shrapnel. Tumbles
// under gravity, clangs off tiles (one bounce), draws the vanilla Empty Bucket
// item sprite. No debuff - just blunt contact damage.
public class EmptyBucketProjectile : ModProjectile
{
	// ModProjectile needs an autoloadable texture even though PreDraw swaps in
	// the vanilla bucket sprite.
	public override string Texture => "GregTechCEuTerraria/Content/Textures/gui/icon/bucket_mode/water_drop";

	public override void SetDefaults()
	{
		Projectile.width = 24;
		Projectile.height = 24;
		Projectile.hostile = true;
		Projectile.friendly = false;
		Projectile.tileCollide = true;
		Projectile.penetrate = 2;     // survives one bounce
		Projectile.timeLeft = 360;
		Projectile.aiStyle = -1;
		Projectile.ignoreWater = true;
	}

	public override void AI()
	{
		Projectile.velocity.Y += 0.25f;
		if (Projectile.velocity.Y > 16f) Projectile.velocity.Y = 16f;
		Projectile.rotation += 0.20f * System.Math.Sign(Projectile.velocity.X == 0 ? 1 : Projectile.velocity.X);
	}

	public override bool OnTileCollide(Vector2 oldVelocity)
	{
		SoundEngine.PlaySound(SoundID.Tink with { Volume = 0.6f }, Projectile.Center);
		Projectile.penetrate--;
		if (Projectile.penetrate <= 0) return true;
		if (oldVelocity.Y != Projectile.velocity.Y) Projectile.velocity.Y = -oldVelocity.Y * 0.5f;
		if (oldVelocity.X != Projectile.velocity.X) Projectile.velocity.X = -oldVelocity.X * 0.6f;
		return false;
	}

	public override bool PreDraw(ref Color lightColor)
	{
		Main.instance.LoadItem(ItemID.EmptyBucket);
		Texture2D tex = TextureAssets.Item[ItemID.EmptyBucket].Value;
		Vector2 pos = Projectile.Center - Main.screenPosition;
		Main.EntitySpriteDraw(tex, pos, null, lightColor, Projectile.rotation, tex.Size() * 0.5f, 1.1f, SpriteEffects.None, 0);
		return false;
	}
}
