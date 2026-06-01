#nullable enable
using System;
using GregTechCEuTerraria.TerrariaCompat.Bosses.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.VacuumFreezer;

// The Vacuum Freezer - the HV-age custom boss, slotted around the mechanical
// bosses difficulty-wise (Queen Slime tier). The frost inverse of the Fallen
// EBF: a floating frost-proof freezer chamber (frostproof casing + freezer
// intake/controller faces, see VacuumFreezerRenderer) on Frozen Wings.
//
// Movement is a deliberate HEAVY STANDOFF LOOM - low speed, high inertia, it
// hangs at a standoff distance and never dashes. The thematic inversion of the
// EBF: instead of closing distance with its body, it lets the SOFT VACUUM PULL
// drag the player in. The difficulty is layered control loss - you're pulled
// while Chilled (slowed) by coolant fog while threading frost-shard volleys - no
// single attack is a punish; the overlap is.
//
// State is held entirely in ai[0..3] (synced) + localAI[0..3] (client visuals),
// so no SendExtraAI is needed.
//   ai[0] = attack state   ai[1] = state timer
//   ai[2] = last attack (avoid repeats)   ai[3] = phase (0 = normal, 1 = supercooled)
[AutoloadBossHead]
public class VacuumFreezer : ModNPC, IDebuggableBoss
{
	private const int S_Hover = 0, S_Pull = 1, S_Fan = 2, S_Coolant = 3,
		S_Quench = 4, S_Stream = 5;

	// Hostile shard damage (separate from the heavier contact damage).
	private const int ShardDamage = 26;

	private static bool _headSwapped;

	// ModNPC needs a real autoloadable texture even though PreDraw draws a
	// composite and returns false - point it at an existing upstream PNG.
	public override string Texture => "GregTechCEuTerraria/Content/Textures/block/casings/solid/machine_casing_frost_proof";

	// Register a boss head (enables the bottom HP bar + minimap/off-screen icon)
	// without shipping custom art - point at an existing GregTech PNG as the
	// load-time placeholder, then swap in the baked core on first draw.
	public override string BossHeadTexture => "GregTechCEuTerraria/Content/Textures/block/casings/solid/machine_casing_frost_proof";

	public override void SetStaticDefaults()
	{
		Main.npcFrameCount[Type] = 1;
		NPCID.Sets.MPAllowedEnemies[Type] = true;
		NPCID.Sets.BossBestiaryPriority.Add(Type);

		// A freezer doesn't care about cold.
		NPCID.Sets.SpecificDebuffImmunity[Type] ??= new bool?[BuffLoader.BuffCount];
		NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.Chilled] = true;
		NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.Frostburn] = true;
		NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.Frozen] = true;

		Language.GetOrRegister("Mods.GregTechCEuTerraria.NPCs.VacuumFreezer.DisplayName", () => "Vacuum Freezer");
		Language.GetOrRegister("Mods.GregTechCEuTerraria.NPCs.VacuumFreezer.Bestiary",
			() => "A sealed freezing chamber that slipped its anchors and took to the air. It doesn't chase - it pulls, fogs the air with coolant, and lets the cold do the work while it vents shards of flash-frozen metal.");
	}

	public override void SetDefaults()
	{
		// 2x bigger for an imposing presence. The sprite is drawn in PreDraw at
		// (renderer size * NPC.scale), INDEPENDENT of width/height, so the hitbox
		// below only controls collision. Inset it well inside the sprite (~70%) -
		// contact damage at the full bounding box feels too big; vanilla bosses
		// use tighter hitboxes than their visual. Both sprite and hitbox stay
		// centred on NPC.Center, so the inset box sits under the chamber core.
		NPC.scale = 2f;
		NPC.width = (int)(VacuumFreezerRenderer.Width * NPC.scale * 0.68f);   // ~130
		NPC.height = (int)(VacuumFreezerRenderer.Height * NPC.scale * 0.72f); // ~184
		NPC.damage = 38;
		NPC.defense = 18;
		NPC.lifeMax = 9000;
		NPC.HitSound = SoundID.NPCHit4;      // metallic clang
		NPC.DeathSound = SoundID.NPCDeath14; // explosion
		NPC.knockBackResist = 0f;
		NPC.noGravity = true;
		NPC.noTileCollide = true;
		NPC.boss = true;
		NPC.npcSlots = 10f;
		NPC.aiStyle = -1;
		NPC.value = Item.buyPrice(gold: 5);
		NPC.SpawnWithHigherTime(30);

		if (!Main.dedServ)
			Music = MusicID.Boss2;
	}

	public override void ApplyDifficultyAndPlayerScaling(int numPlayers, float balance, float bossAdjustment)
	{
		NPC.lifeMax = (int)(NPC.lifeMax * balance * bossAdjustment);
	}

	public override void SetBestiary(BestiaryDatabase database, BestiaryEntry bestiaryEntry)
	{
		bestiaryEntry.Info.Add(new FlavorTextBestiaryInfoElement("Mods.GregTechCEuTerraria.NPCs.VacuumFreezer.Bestiary"));
	}

	public override void ModifyNPCLoot(NPCLoot npcLoot)
	{
		// Signature reward: 15 frost-proof machine casings - the casings for your
		// first real Vacuum Freezer multiblock. Always drops (the boss's own loot,
		// not the configurable "GregTech boss drops" feature).
		if (Mod.TryFind<ModItem>("frostproof_machine_casing", out var casing))
			npcLoot.Add(ItemDropRule.Common(casing.Type, 1, 15, 15));

		// Plus HV-tier "age loot" (tier 3 + components), gated by the
		// EnableBossDrops config flag like every other GregTech boss drop.
		var condition = new BossDrops.BossDropCondition();
		foreach (var d in BossDrops.BossDropRegistry.GetTierDrops(3, withComponents: true))
			npcLoot.Add(new ItemDropWithConditionRule(d.ItemType, chanceDenominator: 1,
				amountDroppedMinimum: d.Min, amountDroppedMaximum: d.Max, condition));
	}

	// ---- AI ----------------------------------------------------------------

	public override void AI()
	{
		UpdateVisuals();

		float glow = NPC.localAI[2];
		Lighting.AddLight(NPC.Center, 0.30f * glow, 0.52f * glow, 0.80f * glow);

		if (!BossAI.TryAcquireTarget(NPC, out Player player))
		{
			Despawn();
			return;
		}
		if (NPC.timeLeft < 1800) NPC.timeLeft = 1800;

		NPC.noGravity = true;
		NPC.noTileCollide = true;

		bool phase2 = NPC.ai[3] >= 1f;
		if (!phase2 && NPC.life < NPC.lifeMax * 0.5f)
		{
			EnterPhase2();
			phase2 = true;
		}

		switch ((int)NPC.ai[0])
		{
			case S_Hover:   Hover(player, phase2);   break;
			case S_Pull:    VacuumPull(player, phase2); break;
			case S_Fan:     Fan(player, phase2);     break;
			case S_Coolant: Coolant(player, phase2); break;
			case S_Quench:  Quench(player, phase2);  break;
			case S_Stream:  Stream(player, phase2);  break;
		}

		BossAI.SmoothTilt(NPC, perVelocity: 0.012f, maxTilt: 0.10f); // heavier body = subtler tilt
	}

	// Heavy standoff loom: hang above the player at a standoff distance with high
	// inertia (low speed, low accel) and a slow sway. It looms, it never dashes -
	// the vacuum pull is what closes distance, not the body.
	private void Hover(Player player, bool phase2)
	{
		float swayDir = ((int)NPC.ai[2] & 1) == 0 ? 1f : -1f;
		float sway = (float)Math.Sin(NPC.ai[1] * 0.04f) * 300f * swayDir; // synced timer (MP-deterministic)
		Vector2 dest = player.Center + new Vector2(sway, -240f);
		BossAI.MoveToward(NPC, dest, phase2 ? 5f : 3.8f, phase2 ? 0.045f : 0.03f, easeRadius: 200f);

		NPC.ai[1]++;
		float wait = phase2 ? 40f : 58f;
		if (NPC.ai[1] >= wait)
		{
			NPC.ai[1] = 0f;
			if (Main.netMode != NetmodeID.MultiplayerClient)
			{
				NPC.ai[0] = PickAttack(phase2, (int)NPC.ai[2]);
				NPC.ai[2] = NPC.ai[0];
				NPC.netUpdate = true;
			}
		}
	}

	private int PickAttack(bool phase2, int last)
	{
		ReadOnlySpan<int> pool = stackalloc int[] { S_Pull, S_Fan, S_Coolant, S_Quench, S_Stream };
		int pick = pool[Main.rand.Next(pool.Length)];
		if (pick == last) pick = pool[Main.rand.Next(pool.Length)]; // one reroll to reduce repeats
		return pick;
	}

	// Soft vacuum pull - drags the player toward the chamber, NEVER a yank: a
	// steady drift they can fight with wings/dashes. Always layered (fog + a few
	// shards) so it's never the only pressure. The pull is applied per-client to
	// the local player (each client moves its own player), so it's MP-correct
	// with no packet - the same model as the vanilla treasure-magnet grab.
	private void VacuumPull(Player player, bool phase2)
	{
		NPC.ai[1]++;
		const int windup = 30;

		// The chamber holds roughly still and intakes - it doesn't move to you.
		NPC.velocity *= 0.94f;
		Telegraph();

		if (NPC.ai[1] < windup)
			return; // glow + vent intake tell before the pull engages

		// Soft, fightable acceleration toward the chamber on the local player.
		if (!Main.dedServ)
		{
			Player lp = Main.player[Main.myPlayer];
			if (lp.active && !lp.dead)
			{
				Vector2 toBoss = NPC.Center - lp.Center;
				float dist = toBoss.Length();
				if (dist > 80f && dist < 1500f)
					lp.velocity += toBoss / dist * (phase2 ? 0.42f : 0.30f);
			}
		}

		// Seed coolant fog around the player + light shard pressure during the pull.
		if (Main.netMode != NetmodeID.MultiplayerClient)
		{
			if ((int)NPC.ai[1] == windup)
			{
				SpawnCoolantCloud(player.Center + new Vector2(-260f, -40f));
				SpawnCoolantCloud(player.Center + new Vector2(260f, -40f));
			}
			int sincePull = (int)NPC.ai[1] - windup;
			if (sincePull % (phase2 ? 16 : 24) == 0)
				FireFan(player, phase2, count: phase2 ? 3 : 2, spreadDeg: 60f, speed: 7.5f);
		}

		int dur = phase2 ? 150 : 120;
		if (NPC.ai[1] >= windup + dur)
			ReturnToHover();
	}

	private void Fan(Player player, bool phase2)
	{
		NPC.ai[1]++;
		NPC.velocity *= 0.93f;

		const int telegraph = 26;
		const int burstEvery = 12;
		int bursts = phase2 ? 3 : 1;

		if (NPC.ai[1] < telegraph) { Telegraph(); return; }

		int sinceTel = (int)NPC.ai[1] - telegraph;
		if (sinceTel % burstEvery == 0 && (sinceTel / burstEvery) < bursts)
		{
			SoundEngine.PlaySound(SoundID.Item27 with { Pitch = 0.5f }, NPC.Center); // all clients
			if (Main.netMode != NetmodeID.MultiplayerClient)
				FireFan(player, phase2, count: phase2 ? 8 : 6, spreadDeg: phase2 ? 56f : 42f, speed: phase2 ? 9.5f : 8f);
		}

		if (NPC.ai[1] >= telegraph + burstEvery * bursts + 10)
			ReturnToHover();
	}

	private void FireFan(Player player, bool phase2, int count, float spreadDeg, float speed)
	{
		if (Main.netMode == NetmodeID.MultiplayerClient) return;

		Vector2 muzzle = NPC.Center + new Vector2(0f, 12f);
		Vector2 baseDir = (player.Center - muzzle).SafeNormalize(Vector2.UnitY);
		float spread = MathHelper.ToRadians(spreadDeg);

		for (int i = 0; i < count; i++)
		{
			float t = count == 1 ? 0.5f : (float)i / (count - 1);
			float ang = MathHelper.Lerp(-spread / 2f, spread / 2f, t);
			SpawnShard(muzzle, baseDir.RotatedBy(ang) * speed, gravity: false);
		}
	}

	// The herding attack: lay down a wall of coolant fog around the player so the
	// safe lane shrinks while the pull tugs and shards fly.
	private void Coolant(Player player, bool phase2)
	{
		NPC.ai[1]++;
		// Drift to a standoff while venting.
		float sway = (float)Math.Sin(NPC.ai[1] * 0.05f) * 160f;
		BossAI.MoveToward(NPC, player.Center + new Vector2(sway, -260f), 4.5f, 0.05f);
		Telegraph();

		const int telegraph = 24;
		if (NPC.ai[1] < telegraph) return;

		if (Main.netMode != NetmodeID.MultiplayerClient)
		{
			int sinceTel = (int)NPC.ai[1] - telegraph;
			int every = phase2 ? 18 : 26;
			if (sinceTel % every == 0)
			{
				int idx = sinceTel / every;
				// Place clouds in a spread around the player (left, right, above lanes).
				float x = idx % 2 == 0 ? -1f : 1f;
				Vector2 at = player.Center + new Vector2(x * Main.rand.Next(160, 340), Main.rand.Next(-160, 60));
				SpawnCoolantCloud(at);
			}
		}

		int dur = phase2 ? 110 : 80;
		if (NPC.ai[1] >= telegraph + dur)
			ReturnToHover();
	}

	// Lob supercooled ingots in arcs; each shatters into a frost-shard spray on
	// landing, so the threat lingers where they fall.
	private void Quench(Player player, bool phase2)
	{
		NPC.ai[1]++;
		float sway = (float)Math.Sin(NPC.ai[1] * 0.06f) * 180f;
		BossAI.MoveToward(NPC, player.Center + new Vector2(sway, -280f), 5f, 0.06f);

		const int telegraph = 24;
		if (NPC.ai[1] < telegraph) { Telegraph(); return; }

		int dur = phase2 ? 100 : 72;
		int every = phase2 ? 14 : 20;
		if (((int)NPC.ai[1] - telegraph) % every == 0)
		{
			SoundEngine.PlaySound(SoundID.Item37 with { Pitch = -0.3f }, NPC.Center); // all clients
			if (Main.netMode != NetmodeID.MultiplayerClient)
			{
				Vector2 muzzle = NPC.Center + new Vector2(Main.rand.Next(-30, 31), 16f);
				Vector2 toP = player.Center - muzzle;
				// Lob: aim partway, let gravity arc it down onto the player.
				Vector2 vel = new(MathHelper.Clamp(toP.X * 0.018f, -7f, 7f), -3f + Main.rand.NextFloat(-1f, 1f));
				SpawnSupercooledIngot(muzzle, vel);
			}
		}
		if (NPC.ai[1] >= telegraph + dur)
			ReturnToHover();
	}

	// A rapid aimed stream of shards with slight jitter - "intake on full blast".
	private void Stream(Player player, bool phase2)
	{
		NPC.ai[1]++;
		NPC.velocity *= 0.9f;

		const int telegraph = 22;
		if (NPC.ai[1] < telegraph) { Telegraph(); return; }

		int dur = phase2 ? 84 : 60;
		int every = phase2 ? 3 : 4;
		if (((int)NPC.ai[1] - telegraph) % every == 0)
		{
			if ((int)NPC.ai[1] % 6 == 0)
				SoundEngine.PlaySound(SoundID.Item27 with { Volume = 0.45f, Pitch = 0.7f }, NPC.Center);
			if (Main.netMode != NetmodeID.MultiplayerClient)
			{
				Vector2 muzzle = NPC.Center + new Vector2(0f, 12f);
				Vector2 dir = (player.Center - muzzle).SafeNormalize(Vector2.UnitY);
				float jitter = MathHelper.ToRadians(Main.rand.NextFloat(-8f, 8f));
				SpawnShard(muzzle, dir.RotatedBy(jitter) * (phase2 ? 11f : 9.5f), gravity: false);
			}
		}
		if (NPC.ai[1] >= telegraph + dur)
			ReturnToHover();
	}

	private void ReturnToHover()
	{
		NPC.ai[0] = S_Hover;
		NPC.ai[1] = 0f;
		NPC.netUpdate = true;
	}

	private void EnterPhase2()
	{
		NPC.ai[3] = 1f;
		NPC.netUpdate = true;
		SoundEngine.PlaySound(SoundID.Item30, NPC.Center); // icy crack
		NPC.localAI[3] = 0.9f; // big glow flash

		if (!Main.dedServ)
			for (int i = 0; i < 28; i++)
			{
				var d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.IceTorch, 0f, 1.5f, 100, default, 1.5f);
				d.noGravity = true;
			}

		// One-time "supercool nova" - a ring of frost shards.
		if (Main.netMode != NetmodeID.MultiplayerClient)
		{
			const int n = 16;
			for (int i = 0; i < n; i++)
				SpawnShard(NPC.Center, (MathHelper.TwoPi * i / n).ToRotationVector2() * 7f, gravity: false);
		}
	}

	private void Despawn()
	{
		BossAI.FlyAwayDespawn(NPC);
		if (NPC.ai[0] != 0f)
		{
			NPC.ai[0] = NPC.ai[1] = NPC.ai[2] = 0f;
			NPC.netUpdate = true;
		}
	}

	// ---- IDebuggableBoss (GTConfig.DebugMobs overlay) ---------------------

	private const float DebugPullRadius = 600f; // approx vacuum pull range (visual only)

	public string CurrentAttackLabel() => StateName((int)NPC.ai[0]);
	public int CurrentPhase() => (int)NPC.ai[3];

	private static string StateName(int s) => s switch
	{
		S_Hover => "Hover", S_Pull => "Pull", S_Fan => "Fan",
		S_Coolant => "Coolant", S_Quench => "Quench", S_Stream => "Stream",
		_ => $"?({s})",
	};

	public void BuildDebugLines(System.Collections.Generic.List<string> lines)
	{
		bool phase2 = NPC.ai[3] >= 1f;
		int state = (int)NPC.ai[0];
		int t = (int)NPC.ai[1];
		float hpPct = NPC.lifeMax > 0 ? 100f * NPC.life / NPC.lifeMax : 0f;
		Player p = Main.player[NPC.target];
		float dist = p?.active == true ? Vector2.Distance(NPC.Center, p.Center) : 0f;
		lines.Add($"Vacuum Freezer  [{(phase2 ? "PHASE 2" : "PHASE 1")}]  HP {NPC.life}/{NPC.lifeMax} ({hpPct:0.0}%)");
		lines.Add($"State: {StateName(state)}   t={t}");
		lines.Add($"Last: {StateName((int)NPC.ai[2])}   Target dist: {dist:0}px");
	}

	public void DrawDebugGizmos(Microsoft.Xna.Framework.Graphics.SpriteBatch sb, Vector2 screenPos)
	{
		Player p = Main.player[NPC.target];
		if (p is null || !p.active) return;
		// Crosshair on the target only - no boss -> player line (screen-spanning
		// diagonals were a screen-blocking rectangle); no pull-radius ring
		// (huge). Numerics are in the text panel.
		DebugOverlaySystem.DrawCrosshair(sb, p.Center, screenPos,
			new Color(160, 230, 255), 10f, 1);
	}

	private void SpawnShard(Vector2 pos, Vector2 vel, bool gravity)
	{
		if (Main.netMode == NetmodeID.MultiplayerClient) return;
		Projectile.NewProjectile(NPC.GetSource_FromAI(), pos, vel,
			ModContent.ProjectileType<FrostShardProjectile>(), ShardDamage, 2f, Main.myPlayer,
			ai0: gravity ? 1f : 0f, ai1: Main.rand.Next(FrostShardProjectile.PaletteCount));
	}

	private void SpawnSupercooledIngot(Vector2 pos, Vector2 vel)
	{
		if (Main.netMode == NetmodeID.MultiplayerClient) return;
		Projectile.NewProjectile(NPC.GetSource_FromAI(), pos, vel,
			ModContent.ProjectileType<SupercooledIngotProjectile>(), ShardDamage, 2f, Main.myPlayer,
			ai0: 1f, ai1: Main.rand.Next(3));
	}

	private void SpawnCoolantCloud(Vector2 pos)
	{
		if (Main.netMode == NetmodeID.MultiplayerClient) return;
		Projectile.NewProjectile(NPC.GetSource_FromAI(), pos,
			new Vector2(Main.rand.NextFloat(-0.6f, 0.6f), 0f),
			ModContent.ProjectileType<CoolantCloudProjectile>(), 0, 0f, Main.myPlayer);
	}

	// Frost-bloom flash + a vent vapor burst - the "about to intake" tell.
	private void Telegraph()
	{
		NPC.localAI[3] = Math.Max(NPC.localAI[3], 0.55f);
		EmitVentVapor(2);
	}

	// Cold vapor spilling DOWNWARD from the intake/controller vents (cold air
	// sinks - the inverse of the EBF's rising exhaust smoke). Client-side.
	private void EmitVentVapor(int count)
	{
		if (Main.dedServ) return;
		// Controller/intake vent sits at the centre of the 3x3 box; cold air sinks,
		// so spill from just below centre.
		Vector2 vent = NPC.Center + new Vector2(0f, VacuumFreezerRenderer.Height * 0.12f * NPC.scale);
		for (int i = 0; i < count; i++)
		{
			Vector2 at = vent + new Vector2(Main.rand.Next(-14, 15) * NPC.scale, Main.rand.Next(-4, 5));
			Vector2 vel = new(Main.rand.NextFloat(-0.5f, 0.5f), Main.rand.NextFloat(0.6f, 1.6f));
			var d = Dust.NewDustPerfect(at, DustID.Cloud, vel, 80, new Color(205, 230, 250), Main.rand.NextFloat(1.3f, 1.9f));
			d.noGravity = true;
			d.fadeIn = 0.4f;
		}
	}

	// ---- visuals (run on all clients) --------------------------------------

	private void UpdateVisuals()
	{
		bool phase2 = NPC.ai[3] >= 1f;

		// Wing frame cycle.
		NPC.localAI[0]++;
		float frameSpeed = phase2 ? 4f : 6f;
		if (NPC.localAI[0] >= frameSpeed)
		{
			NPC.localAI[0] = 0f;
			NPC.localAI[1] = (NPC.localAI[1] + 1f) % 4f;
		}

		// Cold glow pulse: brighter when supercooled (phase 2), plus a decaying
		// telegraph boost.
		float baseG = phase2 ? 0.85f : 0.5f;
		float pulse = baseG + 0.12f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 3f);
		if (NPC.localAI[3] > 0f)
		{
			pulse += NPC.localAI[3];
			NPC.localAI[3] *= 0.9f;
			if (NPC.localAI[3] < 0.02f) NPC.localAI[3] = 0f;
		}
		NPC.localAI[2] = MathHelper.Clamp(pulse, 0f, 1.5f);

		// Constant cold vapor spilling from the vents - heavier when supercooled.
		if (Main.rand.NextBool(phase2 ? 1 : 2))
			EmitVentVapor(1);
	}

	public override void HitEffect(NPC.HitInfo hit)
	{
		int n = NPC.life <= 0 ? 32 : 4;
		for (int i = 0; i < n; i++)
		{
			var d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height,
				DustID.IceTorch, hit.HitDirection, -1f, 100, default, 1.2f);
			d.noGravity = true;
		}
	}

	public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
	{
		// Swap the load-time placeholder boss head for the baked core, once.
		BossHeadHelper.SwapBakedHead(NPC, VacuumFreezerRenderer.BossHeadAsset, ref _headSwapped);

		var body = VacuumFreezerRenderer.Body;
		if (body is null) return true; // composite failed -> fall back to placeholder sprite

		Vector2 pos = NPC.Center - screenPos;
		float scale = NPC.scale;
		var origin = new Vector2(body.Width / 2f, body.Height / 2f);

		float flap = (float)Math.Sin(Main.GlobalTimeWrappedHourly * 4f) * 0.09f;
		VacuumFreezerRenderer.DrawWings(spriteBatch, pos, drawColor, (int)NPC.localAI[1], scale, flap);

		spriteBatch.Draw(body, pos, null, drawColor, NPC.rotation, origin, scale, SpriteEffects.None, 0f);

		var glow = VacuumFreezerRenderer.Glow;
		if (glow is not null)
		{
			float g = NPC.localAI[2];
			spriteBatch.Draw(glow, pos, null, Color.White * MathHelper.Clamp(g, 0f, 1f),
				NPC.rotation, origin, scale, SpriteEffects.None, 0f);
			if (g > 1f) // supercooled: extra bright pass
				spriteBatch.Draw(glow, pos, null, Color.White * (g - 1f),
					NPC.rotation, origin, scale, SpriteEffects.None, 0f);
		}

		return false;
	}
}
