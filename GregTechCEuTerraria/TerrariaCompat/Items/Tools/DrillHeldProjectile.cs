#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Tools;

// Held projectile for drills + chainsaws, adapted from tML ExampleDrillProjectile
// (simplified aiStyle 20). Purely visual - PreDraw renders the wielder's baked
// ToolItem texture so the in-hand sprite matches the inventory icon.
public sealed class DrillHeldProjectile : ModProjectile
{
	// Texture path needed even though PreDraw overrides the draw.
	public override string Texture => "GregTechCEuTerraria/Content/Textures/item/tools/drill";

	// Drill bit points top-left (-3pi/4); these rotate it onto the aim direction.
	// Left hemisphere is FlipHorizontally-mirrored so it gets its own offset
	// (pair chosen so both hemispheres are exact mirror images = drill stays
	// upright at every aim).
	private const float OffsetRight = MathHelper.Pi * 0.75f;
	private const float OffsetLeft  = MathHelper.PiOver4;

	public override void SetStaticDefaults()
	{
		ProjectileID.Sets.HeldProjDoesNotUsePlayerGfxOffY[Type] = true;
	}

	public override void SetDefaults()
	{
		Projectile.width = 28;
		Projectile.height = 28;
		Projectile.friendly = true;
		Projectile.tileCollide = false;
		Projectile.penetrate = -1;
		Projectile.DamageType = DamageClass.Melee;
		Projectile.ownerHitCheck = true; // can't hit enemies through walls
		Projectile.aiStyle = -1;
		Projectile.hide = true;          // drawn in-hand via player.heldProj
	}

	public override void AI()
	{
		Player player = Main.player[Projectile.owner];
		Projectile.timeLeft = 60;

		if (Projectile.soundDelay <= 0)
		{
			SoundEngine.PlaySound(SoundID.Item23, Projectile.Center);
			Projectile.soundDelay = 20;
		}

		Vector2 playerCenter = player.RotatedRelativePoint(player.MountedCenter);
		if (Main.myPlayer == Projectile.owner)
		{
			if (player.channel && !player.noItems && !player.CCed)
			{
				// Projectile.velocity = holdout offset for held projectiles.
				float holdout = player.HeldItem.shootSpeed * Projectile.scale;
				Vector2 offset = holdout * Vector2.Normalize(Main.MouseWorld - playerCenter);
				if (offset != Projectile.velocity) Projectile.netUpdate = true;
				Projectile.velocity = offset;
			}
			else
			{
				Projectile.Kill();
				return;
			}
		}

		bool faceLeft = Projectile.velocity.X < 0f;
		Projectile.direction = Projectile.spriteDirection = faceLeft ? -1 : 1;
		player.ChangeDir(Projectile.direction);
		player.heldProj = Projectile.whoAmI;
		player.SetDummyItemTime(2);   // keep the item "in use" (mining + AoE)
		Projectile.Center = playerCenter;
		Projectile.rotation = Projectile.velocity.ToRotation() +
			(faceLeft ? OffsetLeft : OffsetRight);
		player.itemRotation = (Projectile.velocity * Projectile.direction).ToRotation();
	}

	public override bool PreDraw(ref Color lightColor)
	{
		Player player = Main.player[Projectile.owner];
		Item held = player.HeldItem;
		if (held is null || held.IsAir) return false;

		var tex = TextureAssets.Item[held.type].Value;
		var origin = new Vector2(tex.Width, tex.Height) * 0.5f;
		var pos = Projectile.Center - Main.screenPosition;
		var fx = Projectile.spriteDirection == -1
			? SpriteEffects.FlipHorizontally : SpriteEffects.None;
		Main.EntitySpriteDraw(tex, pos, null, lightColor, Projectile.rotation, origin,
			Projectile.scale, fx, 0);
		return false;
	}
}
