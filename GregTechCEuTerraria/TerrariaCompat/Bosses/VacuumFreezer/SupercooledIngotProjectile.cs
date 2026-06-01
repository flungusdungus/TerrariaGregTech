#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Bosses.Common;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.VacuumFreezer;

// A brittle supercooled ingot lobbed in an arc by the Vacuum Freezer; on landing
// (tile hit or timeout) it shatters into a low spray of frost shards. The plain
// metallic ingot tinted pale-cyan reads as flash-frozen metal. Arc + dust + draw
// come from GlowingHostileProjectile; this adds the shatter on OnKill.
//
//   ai[0] == 1 -> arc (always set by the spawner);  ai[1] -> palette index.
public class SupercooledIngotProjectile : GlowingHostileProjectile
{
	private const int ShatterShards = 4;
	private const int ShardDamage = 16;

	// pale flash-frozen metal tones.
	private static readonly Color[] _palette =
	{
		new(205, 225, 240),
		new(175, 205, 230),
		new(220, 235, 245),
	};

	public override string Texture => "GregTechCEuTerraria/Content/Textures/item/material_sets/metallic/ingot";
	protected override string OverlayTexturePath => "GregTechCEuTerraria/Content/Textures/item/material_sets/metallic/ingot_overlay";
	protected override Color[] Palette => _palette;

	protected override Vector3 GlowColor => new(0.35f, 0.55f, 0.72f);
	protected override int TrailDustType => DustID.IceTorch;
	protected override float SpinSpeed => 0.18f;       // a tumbling ingot, not a fast-spinning shard
	protected override int HitDebuff => BuffID.Frostburn;
	protected override int HitDebuffDuration => 240;

	public override void OnHitPlayer(Player target, Player.HurtInfo info)
	{
		base.OnHitPlayer(target, info);
		target.AddBuff(BuffID.Chilled, 300);
	}

	public override void OnKill(int timeLeft)
	{
		base.OnKill(timeLeft); // inherited ice-puff + shatter sound

		// Shatter into a low fan of frost shards. Server-only spawn (clients get
		// them via the normal projectile sync), mirroring the boss's own volleys.
		if (Main.netMode == NetmodeID.MultiplayerClient) return;

		for (int i = 0; i < ShatterShards; i++)
		{
			float t = ShatterShards == 1 ? 0.5f : (float)i / (ShatterShards - 1);
			// Spray upward-and-outward (-150deg..-30deg) so shrapnel kicks back up off the ground.
			float ang = MathHelper.Lerp(MathHelper.ToRadians(-150f), MathHelper.ToRadians(-30f), t);
			Vector2 vel = ang.ToRotationVector2() * Main.rand.NextFloat(3.5f, 5f);
			Projectile.NewProjectile(Projectile.GetSource_FromAI(), Projectile.Center, vel,
				ModContent.ProjectileType<FrostShardProjectile>(), ShardDamage, 1f, Main.myPlayer,
				ai0: 1f, ai1: Main.rand.Next(FrostShardProjectile.PaletteCount));
		}
	}
}
