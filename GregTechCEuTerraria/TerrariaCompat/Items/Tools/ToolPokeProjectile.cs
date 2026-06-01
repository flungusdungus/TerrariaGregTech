#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Tools;

// Held spear-poke (aiStyle 19) for screwdriver / file. Sprite swapped to the
// wielder's baked tool icon via PreDraw; no per-projectile sync needed (the
// spear is bound to the swing).
public sealed class ToolPokeProjectile : ModProjectile
{
	public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.Spear;

	public override void SetDefaults()
	{
		Projectile.width = 18;
		Projectile.height = 18;
		Projectile.aiStyle = ProjAIStyleID.Spear;
		Projectile.friendly = true;
		Projectile.penetrate = -1;
		Projectile.scale = 1.1f;
		Projectile.alpha = 0;
		Projectile.hide = false;
		Projectile.ownerHitCheck = true;  // can't hit through walls
		Projectile.DamageType = DamageClass.Melee;
		Projectile.tileCollide = false;
		AIType = ProjectileID.Spear;
	}

	public override bool PreDraw(ref Color lightColor)
	{
		var owner = Main.player[Projectile.owner];
		int toolType = owner.HeldItem?.type ?? 0;
		if (toolType <= 0 || toolType >= TextureAssets.Item.Length) return true;

		if (owner.HeldItem!.ModItem is ToolItem tool) tool.EnsureTextureBaked();

		var tex = TextureAssets.Item[toolType].Value;
		// Vanilla spear convention - handle (bottom-left) anchored on the thrust.
		float rot = Projectile.velocity.ToRotation() + MathHelper.PiOver4;
		var origin = new Vector2(0f, tex.Height);
		Main.EntitySpriteDraw(tex, Projectile.Center - Main.screenPosition, null,
			lightColor, rot, origin, Projectile.scale, SpriteEffects.None, 0);
		return false;
	}
}
