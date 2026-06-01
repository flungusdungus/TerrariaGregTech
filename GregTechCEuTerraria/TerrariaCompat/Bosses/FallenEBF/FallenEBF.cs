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

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.FallenEBF;

// The Fallen EBF - the first custom GregTech boss. A flying Electric Blast
// Furnace (heatproof casing + cupronickel coils + muffler + EBF controller
// face, see FallenEBFRenderer) on spectral wings. It fights by smelting:
// telegraphs with a coil-glow flash + muffler smoke, then dumps a volley of
// freshly-smelted hot ingots. Progression slot: between Eye of Cthulhu and
// Skeletron (LV age). Summoned by the Overburned Coil Block.
//
// State is held entirely in ai[0..3] (synced) + localAI[0..3] (client visuals),
// so no SendExtraAI is needed.
//   ai[0] = attack state   ai[1] = state timer
//   ai[2] = last attack (avoid repeats)   ai[3] = phase (0 = normal, 1 = overclock)
[AutoloadBossHead]
public class FallenEBF : ModNPC, IDebuggableBoss
{
	private const int S_Hover = 0, S_Fan = 1, S_Charge = 2, S_Pour = 3,
		S_Rain = 4, S_Spiral = 5, S_MachineGun = 6;

	// Hostile ingot damage (separate from the heavier contact damage).
	private const int IngotDamage = 22;

	private static bool _headSwapped;

	// ModNPC needs a real autoloadable texture even though PreDraw draws a
	// composite and returns false - point it at an existing upstream PNG.
	public override string Texture => "GregTechCEuTerraria/Content/Textures/block/casings/solid/machine_casing_heatproof";

	// Register a boss head (enables the bottom HP bar + minimap/off-screen
	// icon) without shipping custom art - point at an existing GregTech PNG as
	// the load-time placeholder, then swap in the baked core on first draw.
	public override string BossHeadTexture => "GregTechCEuTerraria/Content/Textures/block/casings/solid/machine_casing_heatproof";

	public override void SetStaticDefaults()
	{
		Main.npcFrameCount[Type] = 1;
		NPCID.Sets.MPAllowedEnemies[Type] = true;
		NPCID.Sets.BossBestiaryPriority.Add(Type);

		// A furnace doesn't care about fire.
		NPCID.Sets.SpecificDebuffImmunity[Type] ??= new bool?[BuffLoader.BuffCount];
		NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.OnFire] = true;
		NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.OnFire3] = true;
		NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.Burning] = true;

		Language.GetOrRegister("Mods.GregTechCEuTerraria.NPCs.FallenEBF.DisplayName", () => "Fallen EBF");
		Language.GetOrRegister("Mods.GregTechCEuTerraria.NPCs.FallenEBF.Bestiary",
			() => "A blast furnace that overheated, tore itself from its coils, and took flight. It fights by smelting - and pours the results at anything nearby.");
	}

	public override void SetDefaults()
	{
		// 2x bigger for a more epic presence. The sprite is drawn in PreDraw at
		// (renderer size * NPC.scale) = 192x256, INDEPENDENT of width/height, so
		// the hitbox below only controls collision. Inset it well inside the
		// sprite (~70%) - contact damage at the full bounding box feels too big;
		// vanilla bosses use hitboxes tighter than their visual. Both the sprite
		// and the hitbox stay centred on NPC.Center, so the inset box sits under
		// the machine core.
		NPC.scale = 2f;
		NPC.width = (int)(FallenEBFRenderer.Width * NPC.scale * 0.68f);   // ~130
		NPC.height = (int)(FallenEBFRenderer.Height * NPC.scale * 0.72f); // ~184
		NPC.damage = 30;
		NPC.defense = 12;
		NPC.lifeMax = 3300;
		NPC.HitSound = SoundID.NPCHit4;     // metallic clang
		NPC.DeathSound = SoundID.NPCDeath14; // explosion
		NPC.knockBackResist = 0f;
		NPC.noGravity = true;
		NPC.noTileCollide = true;
		NPC.boss = true;
		NPC.npcSlots = 10f;
		NPC.aiStyle = -1;
		NPC.value = Item.buyPrice(gold: 3);
		NPC.SpawnWithHigherTime(30);

		if (!Main.dedServ)
			Music = MusicID.Boss1;
	}

	public override void ApplyDifficultyAndPlayerScaling(int numPlayers, float balance, float bossAdjustment)
	{
		// Base 3300 -> ~4500 in expert single-player via tML's balance factor.
		NPC.lifeMax = (int)(NPC.lifeMax * balance * bossAdjustment);
	}

	public override void SetBestiary(BestiaryDatabase database, BestiaryEntry bestiaryEntry)
	{
		bestiaryEntry.Info.Add(new FlavorTextBestiaryInfoElement("Mods.GregTechCEuTerraria.NPCs.FallenEBF.Bestiary"));
	}

	public override void ModifyNPCLoot(NPCLoot npcLoot)
	{
		// Signature reward: 15 cupronickel coil blocks - the coils for your
		// first real EBF. Always drops (the boss's own loot, not the
		// configurable "GregTech boss drops" feature).
		if (Mod.TryFind<ModItem>("cupronickel_coil_block", out var coil))
			npcLoot.Add(ItemDropRule.Common(coil.Type, 1, 15, 15));

		// Plus the same LV-tier "age loot" Eye of Cthulhu drops (tier 1 +
		// components), gated by the EnableBossDrops config flag like every
		// other GregTech boss drop. Mirrors the registry so a tier change
		// shifts both together.
		var condition = new BossDrops.BossDropCondition();
		foreach (var d in BossDrops.BossDropRegistry.GetTierDrops(1, withComponents: true))
			npcLoot.Add(new ItemDropWithConditionRule(d.ItemType, chanceDenominator: 1,
				amountDroppedMinimum: d.Min, amountDroppedMaximum: d.Max, condition));
	}

	public override void OnKill()
	{
		// Mark the world flag (gates her normal town spawn + recipes) and spawn her
		// from the wreckage. DoDeathEvents (which calls OnKill) only runs server-side
		// / in SP, so this is server-authoritative. She re-emerges on every kill while
		// she isn't already present (wandering or housed) - see EBFChanSpawner.
		FallenEBFWorld.MarkDowned();
		NPCs.EBFChan.EBFChanSpawner.SpawnFromBossDeath(NPC.Center);
		Mod.Logger.Info($"[EBF-chan] Fallen EBF defeated at {NPC.Center} -> Downed = {FallenEBFWorld.Downed}");
	}

	// ---- AI ----------------------------------------------------------------

	public override void AI()
	{
		UpdateVisuals();

		float glow = NPC.localAI[2];
		Lighting.AddLight(NPC.Center, 0.9f * glow, 0.45f * glow, 0.12f * glow);

		// Target + despawn (two-pass).
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
			case S_Hover:      Hover(player, phase2);      break;
			case S_Fan:        Fan(player, phase2);        break;
			case S_Charge:     ChargeAttack(player, phase2); break;
			case S_Pour:       Pour(player, phase2);       break;
			case S_Rain:       Rain(player, phase2);       break;
			case S_Spiral:     Spiral(player, phase2);     break;
			case S_MachineGun: MachineGun(player, phase2); break;
		}

		BossAI.SmoothTilt(NPC);
	}

	private void Hover(Player player, bool phase2)
	{
		// Weave side-to-side above the player instead of locking dead-centre,
		// and chase loosely (low accel) so it lags behind player movement.
		// Driven by the SYNCED state timer (not a wall clock) so server and MP
		// clients steer the identical path - no rubberbanding. Direction varies
		// per hover via the (synced) last-attack parity.
		float swayDir = ((int)NPC.ai[2] & 1) == 0 ? 1f : -1f;
		float sway = (float)Math.Sin(NPC.ai[1] * 0.06f) * 320f * swayDir;
		Vector2 dest = player.Center + new Vector2(sway, -200f);
		BossAI.MoveToward(NPC, dest, phase2 ? 10f : 7.5f, phase2 ? 0.09f : 0.06f);

		NPC.ai[1]++;
		float wait = phase2 ? 38f : 60f;
		if (NPC.ai[1] >= wait)
		{
			NPC.ai[1] = 0f;
			// Server picks the next attack at random (clients wait for the
			// synced ai[0]); avoids the old fixed Fan->Charge->Pour cycle.
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
		// Pour is a phase-2 overhead-rain finisher; the rest are available from
		// the start so the fight feels varied immediately.
		ReadOnlySpan<int> pool = phase2
			? stackalloc int[] { S_Fan, S_Charge, S_Pour, S_Rain, S_Spiral, S_MachineGun }
			: stackalloc int[] { S_Fan, S_Charge, S_Rain, S_Spiral, S_MachineGun };

		int pick = pool[Main.rand.Next(pool.Length)];
		if (pick == last) pick = pool[Main.rand.Next(pool.Length)]; // one reroll to reduce repeats
		return pick;
	}

	private void Fan(Player player, bool phase2)
	{
		NPC.ai[1]++;
		NPC.velocity *= 0.92f;

		const int telegraph = 28;
		const int burstEvery = 12;
		int bursts = phase2 ? 3 : 1;

		if (NPC.ai[1] < telegraph)
		{
			Telegraph();
			return;
		}

		int sinceTel = (int)NPC.ai[1] - telegraph;
		if (sinceTel % burstEvery == 0 && (sinceTel / burstEvery) < bursts)
		{
			SoundEngine.PlaySound(SoundID.Item20, NPC.Center); // all clients hear it
			FireFan(player, phase2);                           // server-only spawn
		}

		if (NPC.ai[1] >= telegraph + burstEvery * bursts + 10)
			ReturnToHover();
	}

	private void FireFan(Player player, bool phase2)
	{
		if (Main.netMode == NetmodeID.MultiplayerClient) return;

		Vector2 muzzle = NPC.Center + new Vector2(0f, 10f);
		Vector2 baseDir = (player.Center - muzzle).SafeNormalize(Vector2.UnitY);

		int count = phase2 ? 7 : 5;
		float spread = MathHelper.ToRadians(phase2 ? 52f : 38f);
		float speed = phase2 ? 9.5f : 8f;

		for (int i = 0; i < count; i++)
		{
			float t = count == 1 ? 0.5f : (float)i / (count - 1);
			float ang = MathHelper.Lerp(-spread / 2f, spread / 2f, t);
			SpawnIngot(muzzle, baseDir.RotatedBy(ang) * speed, gravity: false);
		}
	}

	private void ChargeAttack(Player player, bool phase2)
	{
		NPC.ai[1]++;
		const int windup = 26;
		const int dash = 26;

		if (NPC.ai[1] < windup)
		{
			NPC.velocity *= 0.9f;
			Telegraph();
			if ((int)NPC.ai[1] == windup - 1)
			{
				Vector2 dir = (player.Center - NPC.Center).SafeNormalize(Vector2.UnitX);
				NPC.velocity = dir * (phase2 ? 19f : 15f);
				SoundEngine.PlaySound(SoundID.ForceRoarPitched, NPC.Center);
			}
			return;
		}

		if (NPC.ai[1] >= windup + dash)
		{
			NPC.velocity *= 0.93f;
			if (NPC.ai[1] >= windup + dash + 14)
				ReturnToHover();
		}
	}

	private void Pour(Player player, bool phase2)
	{
		NPC.ai[1]++;
		float pourSway = (float)Math.Sin(NPC.ai[1] * 0.08f) * 160f; // synced timer (MP-deterministic)
		Vector2 dest = player.Center + new Vector2(pourSway, -270f);
		BossAI.MoveToward(NPC, dest, 9f, 0.1f);

		if (Main.netMode != NetmodeID.MultiplayerClient && NPC.ai[1] % 7f == 0f)
		{
			Vector2 muzzle = NPC.Center + new Vector2(Main.rand.Next(-40, 41), 18f);
			SpawnIngot(muzzle, new Vector2(Main.rand.NextFloat(-1.6f, 1.6f), 2f), gravity: true);
		}
		Telegraph();

		if (NPC.ai[1] >= 120f)
			ReturnToHover();
	}

	// Ingots rain down from the sky across a wide band over the player.
	private void Rain(Player player, bool phase2)
	{
		NPC.ai[1]++;
		float rainSway = (float)Math.Sin(NPC.ai[1] * 0.07f) * 200f; // synced timer (MP-deterministic)
		BossAI.MoveToward(NPC, player.Center + new Vector2(rainSway, -260f), 9f, 0.09f);

		const int telegraph = 24;
		if (NPC.ai[1] < telegraph) { Telegraph(); return; }

		int dur = phase2 ? 95 : 70;
		int every = phase2 ? 5 : 8;
		if (Main.netMode != NetmodeID.MultiplayerClient && ((int)NPC.ai[1] - telegraph) % every == 0)
		{
			float x = player.Center.X + Main.rand.Next(-440, 441);
			Vector2 pos = new(x, player.Center.Y - 540f);
			SpawnIngot(pos, new Vector2(Main.rand.NextFloat(-1f, 1f), Main.rand.NextFloat(4f, 7f)), gravity: true);
		}
		if (NPC.ai[1] >= telegraph + dur) ReturnToHover();
	}

	// A rotating spiral of ingots radiating from the core.
	private void Spiral(Player player, bool phase2)
	{
		NPC.ai[1]++;
		NPC.velocity *= 0.95f; // hold roughly still while spinning

		const int telegraph = 20;
		if (NPC.ai[1] < telegraph) { Telegraph(); return; }

		int dur = phase2 ? 110 : 80;
		int every = phase2 ? 3 : 4;
		int arms = phase2 ? 3 : 2;
		if (Main.netMode != NetmodeID.MultiplayerClient && ((int)NPC.ai[1] - telegraph) % every == 0)
		{
			// Deterministic angle from the timer so server-only spawns stay coherent.
			float baseAng = ((int)NPC.ai[1] - telegraph) * 0.35f;
			for (int a = 0; a < arms; a++)
			{
				float ang = baseAng + MathHelper.TwoPi * a / arms;
				SpawnIngot(NPC.Center, ang.ToRotationVector2() * 6.5f, gravity: false);
			}
		}
		if (NPC.ai[1] >= telegraph + dur) ReturnToHover();
	}

	// A rapid aimed stream with slight jitter - the "smelter on full blast".
	private void MachineGun(Player player, bool phase2)
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
				SoundEngine.PlaySound(SoundID.Item11 with { Volume = 0.45f }, NPC.Center); // all clients
			if (Main.netMode != NetmodeID.MultiplayerClient)
			{
				Vector2 muzzle = NPC.Center + new Vector2(0f, 10f);
				Vector2 dir = (player.Center - muzzle).SafeNormalize(Vector2.UnitY);
				float jitter = MathHelper.ToRadians(Main.rand.NextFloat(-7f, 7f));
				SpawnIngot(muzzle, dir.RotatedBy(jitter) * (phase2 ? 11f : 9.5f), gravity: false);
			}
		}
		if (NPC.ai[1] >= telegraph + dur) ReturnToHover();
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
		SoundEngine.PlaySound(SoundID.ForceRoar, NPC.Center);
		NPC.localAI[3] = 0.9f; // big glow flash

		if (!Main.dedServ)
			for (int i = 0; i < 24; i++)
			{
				var d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.Smoke, 0f, -2f, 120, default, 1.4f);
				d.noGravity = true;
			}

		// One-time "overclock nova" - a ring of ingots.
		if (Main.netMode != NetmodeID.MultiplayerClient)
		{
			const int n = 14;
			for (int i = 0; i < n; i++)
				SpawnIngot(NPC.Center, (MathHelper.TwoPi * i / n).ToRotationVector2() * 7f, gravity: false);
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

	public string CurrentAttackLabel() => StateName((int)NPC.ai[0]);
	public int CurrentPhase() => (int)NPC.ai[3];

	private static string StateName(int s) => s switch
	{
		S_Hover => "Hover", S_Fan => "Fan", S_Charge => "Charge", S_Pour => "Pour",
		S_Rain => "Rain", S_Spiral => "Spiral", S_MachineGun => "MachineGun",
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
		lines.Add($"Fallen EBF  [{(phase2 ? "PHASE 2" : "PHASE 1")}]  HP {NPC.life}/{NPC.lifeMax} ({hpPct:0.0}%)");
		lines.Add($"State: {StateName(state)}   t={t}");
		lines.Add($"Last: {StateName((int)NPC.ai[2])}   Target dist: {dist:0}px");
	}

	public void DrawDebugGizmos(Microsoft.Xna.Framework.Graphics.SpriteBatch sb, Vector2 screenPos)
	{
		Player p = Main.player[NPC.target];
		if (p is null || !p.active) return;
		// Crosshair only - the boss -> player line spans the whole screen
		// diagonally and reads as a screen-blocking rectangle. Distance is in
		// the text panel instead.
		DebugOverlaySystem.DrawCrosshair(sb, p.Center, screenPos,
			new Color(255, 140, 80), 10f, 1);
	}

	private void SpawnIngot(Vector2 pos, Vector2 vel, bool gravity)
	{
		if (Main.netMode == NetmodeID.MultiplayerClient) return;
		Projectile.NewProjectile(NPC.GetSource_FromAI(), pos, vel,
			ModContent.ProjectileType<HotIngotProjectile>(), IngotDamage, 2f, Main.myPlayer,
			ai0: gravity ? 1f : 0f, ai1: Main.rand.Next(HotIngotProjectile.PaletteCount));
	}

	// Coil-glow flash + a heavier smoke burst - the "about to smelt a batch" tell.
	private void Telegraph()
	{
		NPC.localAI[3] = Math.Max(NPC.localAI[3], 0.55f);
		EmitMufflerSmoke(2);
	}

	// Smoke billowing from the muffler (top-centre cell of the body). Spawns
	// client-side; `count` particles per call.
	private void EmitMufflerSmoke(int count)
	{
		if (Main.dedServ) return;
		// Muffler cell centre = top row of the 4-row body, drawn at NPC.scale.
		Vector2 muffler = NPC.Center + new Vector2(0f, -FallenEBFRenderer.Height * 0.375f * NPC.scale);
		for (int i = 0; i < count; i++)
		{
			Vector2 at = muffler + new Vector2(Main.rand.Next(-12, 13) * NPC.scale, Main.rand.Next(-4, 5));
			Vector2 vel = new(Main.rand.NextFloat(-0.7f, 0.7f), Main.rand.NextFloat(-2.6f, -1.3f));
			var d = Dust.NewDustPerfect(at, DustID.Smoke, vel, 60, Color.Gray, Main.rand.NextFloat(1.5f, 2.2f));
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
		float frameSpeed = phase2 ? 3f : 5f;
		if (NPC.localAI[0] >= frameSpeed)
		{
			NPC.localAI[0] = 0f;
			NPC.localAI[1] = (NPC.localAI[1] + 1f) % 4f;
		}

		// Glow pulse: hotter in phase 2, plus a decaying telegraph boost.
		float baseG = phase2 ? 0.85f : 0.5f;
		float pulse = baseG + 0.12f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 4f);
		if (NPC.localAI[3] > 0f)
		{
			pulse += NPC.localAI[3];
			NPC.localAI[3] *= 0.9f;
			if (NPC.localAI[3] < 0.02f) NPC.localAI[3] = 0f;
		}
		NPC.localAI[2] = MathHelper.Clamp(pulse, 0f, 1.5f);

		// Constant exhaust from the muffler - heavier when overheating (phase 2).
		if (Main.rand.NextBool(phase2 ? 1 : 2))
			EmitMufflerSmoke(1);
	}

	public override void HitEffect(NPC.HitInfo hit)
	{
		int n = NPC.life <= 0 ? 30 : 4;
		for (int i = 0; i < n; i++)
		{
			var d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height,
				NPC.life <= 0 ? DustID.Torch : DustID.Smoke, hit.HitDirection, -1f, 100, default, 1.2f);
			d.noGravity = true;
		}
	}

	public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
	{
		// Swap the load-time placeholder boss head for the baked core, once.
		BossHeadHelper.SwapBakedHead(NPC, FallenEBFRenderer.BossHeadAsset, ref _headSwapped);

		var body = FallenEBFRenderer.Body;
		if (body is null) return true; // composite failed -> fall back to placeholder sprite

		Vector2 pos = NPC.Center - screenPos;
		float scale = NPC.scale;
		var origin = new Vector2(body.Width / 2f, body.Height / 2f);

		float flap = (float)Math.Sin(Main.GlobalTimeWrappedHourly * 5f) * 0.10f;
		FallenEBFRenderer.DrawWings(spriteBatch, pos, drawColor, (int)NPC.localAI[1], scale, flap);

		spriteBatch.Draw(body, pos, null, drawColor, NPC.rotation, origin, scale, SpriteEffects.None, 0f);

		var glow = FallenEBFRenderer.Glow;
		if (glow is not null)
		{
			float g = NPC.localAI[2];
			spriteBatch.Draw(glow, pos, null, Color.White * MathHelper.Clamp(g, 0f, 1f),
				NPC.rotation, origin, scale, SpriteEffects.None, 0f);
			if (g > 1f) // overheating: extra bright pass
				spriteBatch.Draw(glow, pos, null, Color.White * (g - 1f),
					NPC.rotation, origin, scale, SpriteEffects.None, 0f);
		}

		return false;
	}
}
