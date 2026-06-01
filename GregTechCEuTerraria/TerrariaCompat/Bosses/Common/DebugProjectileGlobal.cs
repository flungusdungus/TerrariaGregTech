#nullable enable
using GregTechCEuTerraria.Config;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.Common;

// Draws a 1-px red hitbox outline + type label on every HOSTILE projectile
// while GTConfig.DebugMobs is on. Filters out:
//   * friendly projectiles (player weapons, summon minions) - they'd flood the
//     screen with blue boxes during normal combat;
//   * neutral projectiles (visual-only / pickup / marker - e.g. FuseFlame,
//     ImplosionTether beam) - they aren't threats so the box adds noise.
//
// Result: you see ONLY the things that can hit you, which is what you're
// debugging during a boss fight. Zero gameplay effect.
public class DebugProjectileGlobal : GlobalProjectile
{
	public override bool InstancePerEntity => false;

	// Label-suppression threshold: small projectiles (<= this many px on a side)
	// don't get the type label drawn since dense volleys of small bullets cluster
	// and stack the text into an unreadable mess. The box is still drawn.
	private const int LabelMinSize = 60;

	public override void PostDraw(Projectile projectile, Color lightColor)
	{
		if (!GTConfig.Instance.DebugMobs) return;
		if (!projectile.hostile || projectile.friendly) return;

		// Dim red - opaque was too dominant against bright projectile sprites.
		var c = new Color(255, 80, 80) * 0.55f;
		var hb = projectile.Hitbox;
		var screen = new Rectangle(
			(int)(hb.X - Main.screenPosition.X),
			(int)(hb.Y - Main.screenPosition.Y),
			hb.Width, hb.Height);

		DebugOverlaySystem.DrawRectBorder(Main.spriteBatch, screen, c, 1);

		// Type label - skipped on small projectiles (the cluttered swarm case).
		if (hb.Width >= LabelMinSize || hb.Height >= LabelMinSize)
		{
			DynamicSpriteFont font = FontAssets.MouseText.Value;
			string label = projectile.ModProjectile?.Name ?? $"vanilla:{projectile.type}";
			DebugOverlaySystem.DrawShadowed(Main.spriteBatch, font, label,
				new Vector2(screen.X, screen.Y - 12), c, 0.55f);
		}
	}
}
