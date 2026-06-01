#nullable enable
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.ImplosionPress.Projectiles;

// A thick vertical pillar of crushing force descending from the boss to the
// ground. Three-stage timeline:
//
//   0..WindupTicks   - thin telegraph beam (no damage)
//   WindupTicks..ActiveEnd - full-width damaging column (rectangular hitbox)
//   ActiveEnd..Lifetime    - fade out
//
// Boss positions the column's X at a chosen point and width auto-spans from the
// boss bottom downward to the first solid tile (capped at MaxHeightPx).
// Inspired by Empress / Yharon column attacks + Calamity's brimstone pillars.
//
//   ai[0] = age
//   ai[1] = resolved column height in px (computed once on spawn)
public class CompressionColumnProjectile : ModProjectile
{
	// ---- tunables ---------------------------------------------------------
	public const int WindupTicks = 35;   // ~0.58s telegraph
	public const int ActiveEnd  = 65;    // ~0.5s damage window
	public const int Lifetime   = 90;    // fade tail
	public const float ColumnWidth = 84f;
	public const float MaxHeightPx = 1200f;

	public override string Texture => "GregTechCEuTerraria/Content/Textures/gui/icon/bucket_mode/water_drop";

	public override void SetDefaults()
	{
		Projectile.width = (int)ColumnWidth;
		Projectile.height = 2; // hitbox computed via Colliding override
		Projectile.hostile = true;
		Projectile.friendly = false;
		Projectile.tileCollide = false;
		Projectile.penetrate = -1;
		Projectile.timeLeft = Lifetime;
		Projectile.aiStyle = -1;
		Projectile.ignoreWater = true;
	}

	public override void OnSpawn(Terraria.DataStructures.IEntitySource src)
	{
		// Resolve actual column extent (capped at MaxHeightPx).
		Projectile.ai[1] = ResolveColumnHeight();
		if (!Main.dedServ)
			SoundEngine.PlaySound(SoundID.Item62 with { Pitch = 0.5f, Volume = 0.7f }, Projectile.Center);
	}

	public override void AI()
	{
		Projectile.ai[0]++;
		Projectile.velocity = Vector2.Zero;

		int t = (int)Projectile.ai[0];

		// Slam sound at start of active phase.
		if (t == WindupTicks && !Main.dedServ)
		{
			SoundEngine.PlaySound(SoundID.Item62 with { Volume = 1.0f }, Projectile.Center);
			SoundEngine.PlaySound(SoundID.NPCHit42 with { Volume = 0.8f }, Projectile.Center);
			Main.instance.CameraModifiers.Add(new Terraria.Graphics.CameraModifiers.PunchCameraModifier(
				Projectile.Center, Vector2.UnitY, 8f, 4f, 12, 1800f, FullName));
		}

		// Ambient ember dust during active phase.
		if (t >= WindupTicks && t <= ActiveEnd && !Main.dedServ && Main.rand.NextBool(2))
		{
			float dropY = Main.rand.NextFloat(0f, Projectile.ai[1]);
			Vector2 at = Projectile.Center + new Vector2(Main.rand.NextFloat(-ColumnWidth / 2f, ColumnWidth / 2f), dropY);
			var d = Dust.NewDustPerfect(at, DustID.Torch, new Vector2(0, -2f), 100, default, 1.5f);
			d.noGravity = true;
		}

		Lighting.AddLight(Projectile.Center + new Vector2(0, Projectile.ai[1] / 2f),
			0.6f, 0.25f, 0.10f);
	}

	public override bool? CanDamage()
	{
		int t = (int)Projectile.ai[0];
		return (t >= WindupTicks && t <= ActiveEnd) ? null : false;
	}

	public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
	{
		int t = (int)Projectile.ai[0];
		if (t < WindupTicks || t > ActiveEnd) return false;
		// Active rect: width ColumnWidth, height = resolved column extent, anchored
		// from projectile top downward.
		var rect = new Rectangle(
			(int)(Projectile.Center.X - ColumnWidth / 2f),
			(int)Projectile.Center.Y,
			(int)ColumnWidth,
			(int)Projectile.ai[1]);
		return targetHitbox.Intersects(rect);
	}

	public override bool PreDraw(ref Color lightColor)
	{
		const int cloudType = ProjectileID.RainCloudRaining;
		Main.instance.LoadProjectile(cloudType);
		Texture2D tex = TextureAssets.Projectile[cloudType].Value;
		int fh = tex.Height / Math.Max(1, Main.projFrames[cloudType]);
		var src = new Rectangle(0, 0, tex.Width, fh);
		Vector2 origin = new(tex.Width / 2f, fh / 2f);

		int t = (int)Projectile.ai[0];
		float h = Projectile.ai[1];

		// Per-stage width + tint.
		float width, opacity;
		Color tint;
		if (t < WindupTicks)
		{
			float k = t / (float)WindupTicks;
			width = MathHelper.Lerp(ColumnWidth * 0.15f, ColumnWidth * 0.55f, k);
			opacity = 0.45f;
			tint = new Color(255, 180, 80);
		}
		else if (t <= ActiveEnd)
		{
			width = ColumnWidth;
			opacity = 0.85f;
			tint = new Color(255, 130, 80);
		}
		else
		{
			float k = MathHelper.Clamp((Lifetime - t) / (float)(Lifetime - ActiveEnd), 0f, 1f);
			width = ColumnWidth * k;
			opacity = 0.5f * k;
			tint = new Color(255, 200, 140);
		}

		// Stack cloud puffs down the column at fixed Y interval.
		const float step = 28f;
		int n = (int)(h / step) + 1;
		Color drawC = tint * opacity;
		for (int i = 0; i < n; i++)
		{
			float y = i * step;
			Vector2 at = Projectile.Center + new Vector2(0, y) - Main.screenPosition;
			float scale = width / tex.Width * 1.6f;
			Main.spriteBatch.Draw(tex, at, src, drawC, 0f, origin, scale, SpriteEffects.None, 0);
		}
		return false;
	}

	private float ResolveColumnHeight()
	{
		int x = (int)(Projectile.Center.X / 16f);
		int y0 = (int)(Projectile.Center.Y / 16f);
		int maxDy = (int)(MaxHeightPx / 16f);
		for (int dy = 0; dy < maxDy; dy++)
		{
			int y = y0 + dy;
			if (y < 0 || y >= Main.maxTilesY) return MaxHeightPx;
			Tile tile = Main.tile[x, y];
			if (tile.HasTile && Main.tileSolid[tile.TileType])
				return dy * 16f;
		}
		return MaxHeightPx;
	}
}
