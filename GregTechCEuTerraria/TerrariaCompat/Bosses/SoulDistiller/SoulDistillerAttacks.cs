#nullable enable
using System;
using GregTechCEuTerraria.TerrariaCompat.Bosses.Common;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.SoulDistiller;

// The Soul Distiller's four attacks, factored out so the parent worm (which
// cycles all four) and the fraction sub-worms (which each specialise in one)
// share one implementation. Each is a single server-side volley + an all-client
// sound; the worm never stops moving, so there's no per-attack state machine.
//
// `fraction` is -1 for the parent (random fraction colour per glob) or 0..3 for
// a sub-worm (its own fraction). The projectile damage is passed in so a tier /
// difficulty tweak lives at the head.
internal static class SoulDistillerAttacks
{
	public const int Spray = 0, Buckets = 1, OilCloud = 2, GasBelch = 3;
	public const int AttackCount = 4;

	public static void Perform(int attack, NPC head, Player target, int fraction, int globDamage, int bucketDamage, int gasDamage)
	{
		switch (attack)
		{
			case Spray:    DoSpray(head, target, fraction, globDamage); break;
			case Buckets:  DoBuckets(head, target, bucketDamage); break;
			case OilCloud: DoOilCloud(head, target, fraction); break;
			case GasBelch: DoGasBelch(head, fraction, gasDamage); break;
		}
	}

	private static int PaletteFor(int fraction) =>
		fraction < 0 ? Main.rand.Next(SoulDistillerRenderer.FractionCount) : fraction;

	// Aimed fan of globs at the player + a few random "spray around" arcing globs.
	private static void DoSpray(NPC head, Player target, int fraction, int damage)
	{
		SoundEngine.PlaySound(SoundID.Item13 with { Pitch = -0.3f }, head.Center); // wet splat
		if (Main.netMode == NetmodeID.MultiplayerClient) return;

		int type = ModContent.ProjectileType<LiquidGlobProjectile>();
		Vector2 from = head.Center;
		Vector2 baseDir = (target.Center - from).SafeNormalize(Vector2.UnitY);

		const int count = 6;
		float spread = MathHelper.ToRadians(46f);
		for (int i = 0; i < count; i++)
		{
			float t = (float)i / (count - 1);
			float ang = MathHelper.Lerp(-spread / 2f, spread / 2f, t);
			Spawn(head, from, baseDir.RotatedBy(ang) * 9.5f, type, damage, arc: false, PaletteFor(fraction));
		}
		for (int k = 0; k < 3; k++)
		{
			float ang = Main.rand.NextFloat(MathHelper.TwoPi);
			Spawn(head, from, ang.ToRotationVector2() * Main.rand.NextFloat(4f, 7f), type, damage, arc: true, PaletteFor(fraction));
		}
	}

	// Lob a few empty buckets in arcs toward the player.
	private static void DoBuckets(NPC head, Player target, int damage)
	{
		SoundEngine.PlaySound(SoundID.Item37 with { Pitch = 0.4f }, head.Center); // metallic
		if (Main.netMode == NetmodeID.MultiplayerClient) return;

		int type = ModContent.ProjectileType<EmptyBucketProjectile>();
		Vector2 from = head.Center;
		Vector2 dir = (target.Center - from).SafeNormalize(Vector2.UnitX);
		for (int i = 0; i < 3; i++)
		{
			Vector2 vel = dir * Main.rand.NextFloat(7f, 10f) + new Vector2(0f, Main.rand.NextFloat(-5f, -2.5f));
			Spawn(head, from, vel, type, damage, arc: false, 0);
		}
	}

	// Drop 1-2 raining oil clouds above the player (heavy-oil signature).
	private static void DoOilCloud(NPC head, Player target, int fraction)
	{
		SoundEngine.PlaySound(SoundID.Item34 with { Volume = 0.6f }, head.Center); // bubbling
		if (Main.netMode == NetmodeID.MultiplayerClient) return;

		int type = ModContent.ProjectileType<OilCloudProjectile>();
		int frac = fraction < 0 ? SoulDistillerRenderer.HeavyOil : fraction;
		int clouds = Main.rand.Next(1, 3);
		for (int i = 0; i < clouds; i++)
		{
			Vector2 pos = target.Center + new Vector2(Main.rand.Next(-220, 221), -260f);
			Spawn(head, pos, new Vector2(Main.rand.NextFloat(-1.2f, 1.2f), 0f), type, 0, arc: false, frac);
		}
	}

	// Heavy tail segments belch rising toxic gas. Walk the chain and vent from
	// the rearmost segments (gradient ratio > 0.55).
	private static void DoGasBelch(NPC head, int fraction, int damage)
	{
		SoundEngine.PlaySound(SoundID.Item16 with { Pitch = -0.5f }, head.Center); // hiss
		if (Main.netMode == NetmodeID.MultiplayerClient) return;

		int type = ModContent.ProjectileType<ToxicGasProjectile>();
		for (int i = 0; i < Main.maxNPCs; i++)
		{
			NPC n = Main.npc[i];
			if (!n.active || n.realLife != head.whoAmI || i == head.whoAmI) continue;
			if (n.ai[2] < 0.55f) continue;                 // only the heavy rear segments
			if (!Main.rand.NextBool(2)) continue;           // not every segment, every belch
			Spawn(head, n.Center, new Vector2(Main.rand.NextFloat(-0.6f, 0.6f), -1.2f), type, damage, arc: false, 0);
		}
	}

	private static void Spawn(NPC head, Vector2 pos, Vector2 vel, int type, int damage, bool arc, int palette)
	{
		Projectile.NewProjectile(head.GetSource_FromAI(), pos, vel, type,
			Math.Max(damage, 1), 2f, Main.myPlayer, ai0: arc ? 1f : 0f, ai1: palette);
	}
}
