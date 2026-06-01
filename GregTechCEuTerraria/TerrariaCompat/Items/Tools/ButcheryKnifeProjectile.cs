#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Tools;

// Vanilla Throwing-Knife projectile (aiStyle 2) repainted with the source
// knife's baked composite via PreDraw.
//
// Source type stamped into ai[0] by ToolItem.Shoot. aiStyle 2 reuses ai[0] as
// its gravity-delay timer (clamps to 40) and destroys it on the first AI tick,
// so PreAI captures it once into _srcType before vanilla AI runs. The spawn
// packet carries the un-clobbered ai[0] so remote clients capture identically.
public sealed class ButcheryKnifeProjectile : ModProjectile
{
	public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.ThrowingKnife;

	private int _srcType = -1;

	public override void SetDefaults()
	{
		Projectile.CloneDefaults(ProjectileID.ThrowingKnife);
		Projectile.DamageType = DamageClass.Melee;
	}

	public override bool PreAI()
	{
		if (_srcType < 0) _srcType = (int)Projectile.ai[0];
		return true;
	}

	public override bool PreDraw(ref Color lightColor)
	{
		if (_srcType <= 0 || _srcType >= TextureAssets.Item.Length) return true;

		var tex = TextureAssets.Item[_srcType].Value;
		var origin = new Vector2(tex.Width, tex.Height) * 0.5f;
		var pos = Projectile.Center - Main.screenPosition;
		var fx = Projectile.spriteDirection == -1
			? SpriteEffects.FlipHorizontally : SpriteEffects.None;
		Main.EntitySpriteDraw(tex, pos, null, lightColor, Projectile.rotation, origin,
			Projectile.scale, fx, 0);
		return false;
	}
}
