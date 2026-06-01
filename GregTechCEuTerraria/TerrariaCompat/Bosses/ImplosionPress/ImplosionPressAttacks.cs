#nullable enable
using System;
using GregTechCEuTerraria.TerrariaCompat.Bosses.ImplosionPress.Projectiles;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.ImplosionPress;

// Per-attack spawn logic for the Implosion Press. Each method is a pure
// tick-driven function over the NPC + target player: called once per AI tick
// while the attack is active, decides which ticks emit, and spawns projectiles
// via Projectile.NewProjectile. SERVER-SIDE ONLY - the boss guards Main.netMode
// before calling.
//
// `t` is ticks since the attack began (any telegraph already elapsed, t >= 0).
// `phase2` is the boss's phase flag (50%-HP gated). All numbers are explicit
// constants so they can be tuned freely. Mirrors CausticReactorAttacks.
internal static class ImplosionPressAttacks
{
	// ---- 1. CRUSH ZONE (background + named) -------------------------------
	// Designates a circle within DesignateOffsetPx of the player.
	public static void SpawnCrushZone(NPC npc, Player player, int damage, bool phase2)
	{
		const float DesignateOffsetPx = 220f;
		float ang = Main.rand.NextFloat(MathHelper.TwoPi);
		float dist = Main.rand.NextFloat(60f, DesignateOffsetPx);
		Vector2 at = player.Center + ang.ToRotationVector2() * dist;
		Projectile.NewProjectile(npc.GetSource_FromAI(), at, Vector2.Zero,
			ModContent.ProjectileType<CrushZoneProjectile>(), damage, 1f, Main.myPlayer,
			ai0: 0f, ai1: phase2 ? 1f : 0f);
	}

	// ---- 2. CARBON FLAK (background) --------------------------------------
	// Muffler lobs N grey arcs toward random ground spots in the arena.
	public static void SpawnCarbonFlakBurst(NPC npc, Player player, int damage, bool phase2)
	{
		int n = phase2 ? Main.rand.Next(4, 7) : Main.rand.Next(3, 6);
		Vector2 mufflerVent = npc.Center + new Vector2(0f, -ImplosionPressRenderer.Height * 0.5f);
		for (int i = 0; i < n; i++)
		{
			// Aim somewhere near the player horizontally, +/- 480 px scatter, falling
			// from above. Random arc trajectory.
			float dx = Main.rand.NextFloat(-480f, 480f);
			Vector2 targetGround = new(player.Center.X + dx, player.Center.Y - 200f);
			Vector2 toTarget = targetGround - mufflerVent;
			float dist = toTarget.Length();
			Vector2 dir = toTarget / Math.Max(dist, 1f);
			// Lob speed ~6-9 px/tick (gravity will bring it down).
			Vector2 vel = dir * Main.rand.NextFloat(5.5f, 8.5f) + new Vector2(0f, -2.5f);
			Projectile.NewProjectile(npc.GetSource_FromAI(), mufflerVent, vel,
				ModContent.ProjectileType<CarbonBlockHazard>(), damage, 0.5f, Main.myPlayer);
		}
	}

	// ---- 3. HELLBLAST VOLLEY (named) ----------------------------------------
	// 5-spread of fast penetrating ITNT orbs aimed at predicted player position.
	// Caller picks the tick to spawn at (e.g. every 36 ticks for 3 volleys).
	public const int HellblastVolleyCount = 3;
	public const int HellblastVolleyInterval = 38;
	public static void SpawnHellblastVolley(NPC npc, Player player, int damage, bool phase2)
	{
		const int spread = 5;
		const float spreadArc = 0.40f; // ~23 deg total
		const float orbSpeed = 9.5f;
		Vector2 lead = PredictedPlayerPos(player, 14);
		Vector2 dir = (lead - npc.Center).SafeNormalize(Vector2.UnitY);
		int type = ModContent.ProjectileType<HellblastOrbProjectile>();
		for (int i = 0; i < spread; i++)
		{
			float frac = (i - (spread - 1) / 2f) / Math.Max(1f, (spread - 1) / 2f); // -1..1
			float ang = frac * spreadArc;
			Vector2 vel = dir.RotatedBy(ang) * orbSpeed * (phase2 ? 1.15f : 1f);
			Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, vel,
				type, damage, 1.5f, Main.myPlayer, ai0: 0f, ai1: phase2 ? 1f : 0f);
		}
	}

	// ---- 4. MORTAR SALVO (named) -------------------------------------------
	// N heavy shells lobbed in arcing trajectories at staggered impact sites.
	public const int MortarSalvoCount = 4;
	public const int MortarSalvoInterval = 26;
	public static void SpawnMortarShell(NPC npc, Player player, int damage, bool phase2, int shellIndex)
	{
		// Each shell aims at a different X offset from the player so impacts spread.
		float dx = ((shellIndex - (MortarSalvoCount - 1) / 2f) * 220f);
		Vector2 target = new(player.Center.X + dx, player.Center.Y + 60f);
		Vector2 toTarget = target - npc.Center;
		// Lobbed: significant vertical component, modest horizontal speed - the
		// gravity term in MortarShell.AI does the rest.
		Vector2 vel = new(toTarget.X * 0.012f, -10f);
		Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, vel,
			ModContent.ProjectileType<MortarShellProjectile>(), damage, 1.5f, Main.myPlayer,
			ai0: 0f, ai1: phase2 ? 1f : 0f);
	}

	// ---- 5. FUSE-LINE CASCADE (named) --------------------------------------
	// Places N charges in a horizontal line below the player + spawns a fuse flame
	// at one end.
	public const int FuseChargeCount = 6;
	public const float FuseChargeSpacing = 180f;
	public static void SpawnFuseLine(NPC npc, Player player, int damage, bool phase2)
	{
		float lineY = player.Center.Y + 220f; // below the player's current Y
		float startX = player.Center.X - (FuseChargeCount - 1) * FuseChargeSpacing / 2f;
		int chargeType = ModContent.ProjectileType<FuseChargeProjectile>();
		for (int i = 0; i < FuseChargeCount; i++)
		{
			Vector2 at = new(startX + i * FuseChargeSpacing, lineY);
			Projectile.NewProjectile(npc.GetSource_FromAI(), at, Vector2.Zero,
				chargeType, damage, 1f, Main.myPlayer);
		}
		// Spawn the fuse flame at the leftmost charge, moving rightward at FuseSpeed.
		Vector2 flameAt = new(startX, lineY);
		Projectile.NewProjectile(npc.GetSource_FromAI(), flameAt,
			new Vector2(FuseFlameProjectile.FuseSpeed, 0f),
			ModContent.ProjectileType<FuseFlameProjectile>(), 0, 0f, Main.myPlayer);
	}

	// ---- 6. PRESSURE PULSE (named) -----------------------------------------
	// Single fast-expanding ring centered on the boss. One-tick spawn.
	public static void SpawnPressurePulse(NPC npc, int damage)
	{
		Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, Vector2.Zero,
			ModContent.ProjectileType<PressureRingProjectile>(), damage, 0f, Main.myPlayer);
	}

	// ---- 7. COMPRESSION COLUMN (named) -------------------------------------
	// One column per call - boss repositions between columns.
	public static void SpawnCompressionColumn(NPC npc, Player player, int damage)
	{
		Vector2 at = new(npc.Center.X, npc.Center.Y + ImplosionPressRenderer.Height * 0.5f);
		Projectile.NewProjectile(npc.GetSource_FromAI(), at, Vector2.Zero,
			ModContent.ProjectileType<CompressionColumnProjectile>(), damage, 1f, Main.myPlayer);
	}

	// ---- 8. IMPLOSION TETHER (named) ---------------------------------------
	public static void SpawnTether(NPC npc, Player player, int damage, bool phase2)
	{
		// Marker = player's current position at spawn. Phase flavour is resolved
		// from the live boss at detonate time (Projectile.ai is float[3], so the
		// tether has no free slot to carry a phase flag - all three slots already
		// hold age / markerX / markerY).
		Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, Vector2.Zero,
			ModContent.ProjectileType<ImplosionTetherProjectile>(), damage, 0f, Main.myPlayer,
			ai0: 0f, ai1: player.Center.X, ai2: player.Center.Y);
	}

	// ---- 9. DETONATOR PRESS (named) ----------------------------------------
	// Body rapid-drops to ground, releases triple shockwave + carbon fan.
	// The boss handles the descent + animation; this method ONLY spawns the
	// three staggered rings + the fan once landing occurs. Called by the boss
	// at the impact tick.
	public static void SpawnDetonatorImpact(NPC npc, int damage, bool phase2)
	{
		// Three concentric rings - one projectile per ring, staggered via a
		// negative ai[0] start so they expand at different rates.
		int ringType = ModContent.ProjectileType<DetonatorShockwaveProjectile>();
		for (int i = 0; i < 3; i++)
		{
			var idx = Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, Vector2.Zero,
				ringType, damage, 0f, Main.myPlayer);
			if (idx < Main.maxProjectiles) Main.projectile[idx].ai[0] = -i * 8f; // staggered start
		}

		// 12-way carbon ejecta fan (ember-gold palette = 3).
		int shardType = ModContent.ProjectileType<CarbonShardProjectile>();
		int shardDmg = Math.Max(1, damage * 60 / 100);
		const int ejecta = 12;
		for (int i = 0; i < ejecta; i++)
		{
			float ang = MathHelper.TwoPi * i / ejecta - MathHelper.PiOver2 * 0.5f;
			Vector2 vel = ang.ToRotationVector2() * 8.5f;
			Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, vel,
				shardType, shardDmg, 1.2f, Main.myPlayer,
				ai0: 1f /* arc */, ai1: 3f /* ember-gold */);
		}
	}

	// ---- 10. DIAMOND FORGE (phase 2 signature) ----------------------------
	// Single giant shockwave with a tiny safe vault at a random arena edge.
	// Player has the projectile's spawn-to-detonate window to reach the vault.
	public const float DiamondForgeVaultDistance = 700f;
	public static void SpawnDiamondForge(NPC npc, Player player, int damage)
	{
		// Vault at a random arena-edge angle from the boss.
		float vaultAng = Main.rand.NextFloat(-MathHelper.Pi, MathHelper.Pi);
		Vector2 vault = npc.Center + vaultAng.ToRotationVector2() * DiamondForgeVaultDistance;

		// Encode vault position in ai[1] / ai[2] for the projectile to read.
		Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, Vector2.Zero,
			ModContent.ProjectileType<DiamondForgeProjectile>(), damage, 0f, Main.myPlayer,
			ai0: 0f, ai1: vault.X, ai2: vault.Y);
	}

	// ---- 11. CHAIN REACTION (phase 2) -------------------------------------
	// 4 Crush Zones in a horizontal line below the player. Each detonates 0.4s
	// after the previous (ai[0] start staggered so their internal timers fire
	// in sequence). Each spawns its own 8-way carbon spray on detonate (already
	// part of CrushZone's behavior).
	public const int ChainCrushCount = 4;
	public const float ChainCrushSpacing = 220f;
	public const int ChainCrushStagger = 24; // ticks between detonations
	public static void SpawnChainReaction(NPC npc, Player player, int damage, bool phase2)
	{
		float startX = player.Center.X - (ChainCrushCount - 1) * ChainCrushSpacing / 2f;
		float y = player.Center.Y + 80f;
		int type = ModContent.ProjectileType<CrushZoneProjectile>();
		for (int i = 0; i < ChainCrushCount; i++)
		{
			Vector2 at = new(startX + i * ChainCrushSpacing, y);
			// Stagger by SETTING initial ai[0] to a negative offset so this zone
			// reaches its detonate stage `i * ChainCrushStagger` ticks AFTER the
			// first. Zone 0 starts at 0 (normal countdown); zone 1 at -stagger;
			// zone 2 at -2*stagger; etc. All start at outline phase (no skip).
			int idx = Projectile.NewProjectile(npc.GetSource_FromAI(), at, Vector2.Zero,
				type, damage, 1f, Main.myPlayer,
				ai0: 0f, ai1: phase2 ? 1f : 0f);
			if (idx < Main.maxProjectiles)
				Main.projectile[idx].ai[0] = -i * ChainCrushStagger;
		}
	}

	// ---- 12. CARBON STORM (phase 2 sustained) -----------------------------
	// Rains carbon shards from offscreen-top at player position. Called per tick
	// during the sustain; caller decides duration.
	public const int CarbonStormInterval = 6;
	public static void SpawnCarbonStormShard(NPC npc, Player player, int damage)
	{
		// Spawn position ~600 px above the player; scatter horizontally.
		Vector2 from = new(player.Center.X + Main.rand.NextFloat(-220f, 220f),
		                   player.Center.Y - 600f);
		Vector2 vel = new(Main.rand.NextFloat(-1.5f, 1.5f), Main.rand.NextFloat(8f, 11f));
		Projectile.NewProjectile(npc.GetSource_FromAI(), from, vel,
			ModContent.ProjectileType<CarbonShardProjectile>(), damage, 0.8f, Main.myPlayer,
			ai0: 1f /* arc-gravity ON */, ai1: 2f /* ITNT red phase-2 palette */);
	}

	// ---- helpers ----------------------------------------------------------
	private static Vector2 PredictedPlayerPos(Player p, int ticksAhead)
	{
		return p.Center + p.velocity * ticksAhead;
	}

}
