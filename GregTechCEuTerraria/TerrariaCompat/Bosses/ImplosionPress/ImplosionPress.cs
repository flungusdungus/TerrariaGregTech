#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Bosses.Common;
using GregTechCEuTerraria.TerrariaCompat.Bosses.ImplosionPress.Projectiles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.ImplosionPress;

// The Implosion Press - the IV-age post-Plantera boss. A 3x3 industrial press
// (top-vented + bottom-emitting, see ImplosionPressRenderer) that anchors above
// the player and weaponises convergent-implosion mechanics rather than the
// outward emissions every other GTCEu-T boss uses. The signature beat is
// Diamond Forge in phase 2 - the entire arena outside a tiny safe vault implodes.
//
// State machine + background producers, mirroring CausticReactor's pattern:
//   ai[0] = state          ai[1] = state timer
//   ai[2] = last attack    ai[3] = phase (0 = pressurization, 1 = diamond forge)
//
// Named attacks (chosen randomly with one anti-repeat reroll) are layered on top
// of ALWAYS-ON background producers (Crush Zone designations + Carbon Flak), so
// even "quiet" moments still have hazards. The fight is multi-track: read the
// named pattern + read the ambient countdowns + manage the slow-accumulating
// carbon block floor.
[AutoloadBossHead]
public class ImplosionPress : ModNPC, IDebuggableBoss
{
	// ---- state machine -----------------------------------------------------
	private const int S_Reposition   = 0;
	private const int S_Hellblast    = 1;
	private const int S_Mortar       = 2;
	private const int S_Fuse         = 3;
	private const int S_PressurePulse = 4;
	private const int S_Column       = 5;
	private const int S_Tether       = 6;
	private const int S_Slam         = 7;
	private const int S_DiamondForge = 8; // phase 2 only
	private const int S_ChainReaction = 9; // phase 2 only

	// ---- tunables (combat) -------------------------------------------------
	// Damages and HP - the single dial for "boss feels too easy / too hard".
	public const int BaseLifeMax = 26000;
	public const int CrushZoneDamage = 38;
	public const int CarbonFlakDamage = 26;
	public const int HellblastDamage = 30;
	public const int MortarDamage = 42;
	public const int FuseChargeDamage = 36;
	public const int PressureRingDamage = 34;
	public const int ColumnDamage = 40;
	public const int TetherDamage = 38;
	public const int SlamDamage = 42;
	public const int DiamondForgeDamage = 70;
	public const int CarbonStormDamage = 22;

	public const int BaseDefense = 28;
	public const int ContactDamage = 50;

	// ---- tunables (timing) ------------------------------------------------
	private const int RepositionGlideTicks = 100; // ~1.7s glide between anchors
	private const float StandoffRadius = 360f;    // hover this far above player
	private const float GlideSpeed = 6.0f;
	private const float GlideAccel = 0.06f;

	// Named-attack durations (after entry; state transitions to Reposition after).
	private const int Dur_Hellblast    = (ImplosionPressAttacks.HellblastVolleyCount - 1) * ImplosionPressAttacks.HellblastVolleyInterval + 30;
	private const int Dur_Mortar       = (ImplosionPressAttacks.MortarSalvoCount - 1) * ImplosionPressAttacks.MortarSalvoInterval + 40;
	private const int Dur_Fuse         = 240; // line lays + flame traverses
	private const int Dur_PressurePulse = 60;  // single spawn + recovery
	private const int Dur_Column       = 280; // 3 columns + repositions
	private const int Dur_Tether       = ImplosionPressTether.SustainTicks + 30;
	private const int Dur_Slam         = 130; // windup + descend + impact + lift
	private const int Dur_DiamondForge = 180;
	private const int Dur_ChainReaction = (ImplosionPressAttacks.ChainCrushCount - 1) * ImplosionPressAttacks.ChainCrushStagger + 240;

	// Background producer cadences (ticks).
	private const int CrushZoneIntervalP1 = 240; // one zone every 4s
	private const int CrushZoneIntervalP2 = 160; // tighter in phase 2
	private const int CrushZoneMaxLiveP1 = 3;
	private const int CrushZoneMaxLiveP2 = 4;
	private const int CarbonFlakIntervalP1 = 200;
	private const int CarbonFlakIntervalP2 = 130;
	private const int CarbonBlockMaxLive = 12; // FIFO cap on stationary hazards

	// Carbon storm: phase-2-only sustained shard rain triggered as a separate
	// background ticker every X seconds, sustains for Y ticks.
	private const int CarbonStormPeriod = 600; // every 10s in phase 2
	private const int CarbonStormDuration = 480; // sustains for 8s

	// Slam attack internal sub-phases (ticks within Dur_Slam).
	private const int SlamWindupTicks = 30;
	private const int SlamLandTick = 60;
	private const int SlamReturnTick = 90;

	// Diamond Forge: phase-2 signature attack, re-fires periodically.
	private const int DiamondForgePeriod = 1500; // 25s

	// ---- runtime state -----------------------------------------------------
	private static bool _headSwapped;
	private float _anchorAngle;      // current anchor angle around player (drift)
	private float _crushZoneTimer;   // ticks until next background CrushZone
	private float _carbonFlakTimer;
	private float _carbonStormTimer; // ticks until next carbon storm period (phase 2)
	private int _carbonStormActive;  // ticks remaining in current storm sustain
	private float _diamondForgeTimer;
	private Vector2 _preSlamAnchor;  // saved anchor to lift back to after slam

	// Rolling history of the last N attacks picked (debug overlay reads this).
	private readonly int[] _attackHistory = new int[5];
	private int _attackHistoryCount;

	public override string Texture => "GregTechCEuTerraria/Content/Textures/block/casings/solid/machine_casing_solid_steel";
	public override string BossHeadTexture => "GregTechCEuTerraria/Content/Textures/block/casings/solid/machine_casing_solid_steel";

	public override void SetStaticDefaults()
	{
		Main.npcFrameCount[Type] = 1;
		NPCID.Sets.MPAllowedEnemies[Type] = true;
		NPCID.Sets.BossBestiaryPriority.Add(Type);

		// Industrial press shrugs off burn / poison / confusion.
		NPCID.Sets.SpecificDebuffImmunity[Type] ??= new bool?[BuffLoader.BuffCount];
		NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.OnFire] = true;
		NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.Poisoned] = true;
		NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.Venom] = true;
		NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.Confused] = true;

		Language.GetOrRegister("Mods.GregTechCEuTerraria.NPCs.ImplosionPress.DisplayName",
			() => "Implosion Press");
		Language.GetOrRegister("Mods.GregTechCEuTerraria.NPCs.ImplosionPress.Bestiary",
			() => "A runaway implosion compressor venting overpressure through its muffler. It doesn't strike from afar - it CONVERGES, dragging space inward until the gaps close and the air implodes. In its final phase it forges diamonds from the arena itself.");
	}

	public override void SetDefaults()
	{
		NPC.scale = 2f;
		NPC.width = (int)(ImplosionPressRenderer.Width * NPC.scale * 0.70f);
		NPC.height = (int)(ImplosionPressRenderer.Height * NPC.scale * 0.70f);
		NPC.damage = ContactDamage;
		NPC.defense = BaseDefense;
		NPC.lifeMax = BaseLifeMax;
		NPC.HitSound = SoundID.NPCHit4;       // metallic clang
		NPC.DeathSound = SoundID.NPCDeath14;  // explosion
		NPC.knockBackResist = 0f;
		NPC.noGravity = true;
		NPC.noTileCollide = true;
		NPC.boss = true;
		NPC.npcSlots = 12f;
		NPC.aiStyle = -1;
		NPC.value = Item.buyPrice(gold: 10);
		NPC.SpawnWithHigherTime(30);

		if (!Main.dedServ)
			Music = MusicID.Boss3;
	}

	public override void ApplyDifficultyAndPlayerScaling(int numPlayers, float balance, float bossAdjustment)
	{
		NPC.lifeMax = (int)(NPC.lifeMax * balance * bossAdjustment);
	}

	public override void SetBestiary(BestiaryDatabase database, BestiaryEntry bestiaryEntry)
	{
		bestiaryEntry.Info.Add(new FlavorTextBestiaryInfoElement("Mods.GregTechCEuTerraria.NPCs.ImplosionPress.Bestiary"));
	}

	public override void ModifyNPCLoot(NPCLoot npcLoot)
	{
		// Signature reward: 15 solid-steel machine casings - enough to start your
		// first Implosion Compressor multiblock. Always drops.
		if (Mod.TryFind<ModItem>("solid_machine_casing", out var casing))
			npcLoot.Add(ItemDropRule.Common(casing.Type, 1, 15, 15));

		// 1x diamond - thematic "forged in the fight" drop.
		npcLoot.Add(ItemDropRule.Common(ItemID.Diamond, 1, 1, 1));

		// Plus IV-tier "age loot" (tier 5 + components), gated by EnableBossDrops.
		var condition = new BossDrops.BossDropCondition();
		foreach (var d in BossDrops.BossDropRegistry.GetTierDrops(5, withComponents: true))
			npcLoot.Add(new ItemDropWithConditionRule(d.ItemType, chanceDenominator: 1,
				amountDroppedMinimum: d.Min, amountDroppedMaximum: d.Max, condition));
	}

	public override void OnKill()
	{
		if (Main.netMode != NetmodeID.MultiplayerClient)
			ImplosionPressWorld.MarkDowned();
	}

	// ---- AI ----------------------------------------------------------------

	public override void AI()
	{
		UpdateVisuals();

		float glow = NPC.localAI[2];
		Lighting.AddLight(NPC.Center + new Vector2(0f, ImplosionPressRenderer.Height * 0.35f),
			0.85f * glow, 0.30f * glow, 0.12f * glow);

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
			EnterPhase2(player);
			phase2 = true;
		}

		switch ((int)NPC.ai[0])
		{
			case S_Reposition:    DoReposition(player);                  break;
			case S_Hellblast:     RunHellblast(player, phase2);          break;
			case S_Mortar:        RunMortar(player, phase2);             break;
			case S_Fuse:          RunFuse(player, phase2);               break;
			case S_PressurePulse: RunPressurePulse(player, phase2);      break;
			case S_Column:        RunColumn(player, phase2);             break;
			case S_Tether:        RunTether(player, phase2);             break;
			case S_Slam:          RunSlam(player, phase2);               break;
			case S_DiamondForge:  RunDiamondForge(player, phase2);       break;
			case S_ChainReaction: RunChainReaction(player, phase2);      break;
		}

		// Background producers run on top of every state EXCEPT during the Slam
		// descent (the boss is busy diving) and Diamond Forge (the screen-wide
		// implosion is the only thing happening).
		if ((int)NPC.ai[0] != S_DiamondForge && !IsSlamDescentActive())
			TickBackground(player, phase2);

		BossAI.SmoothTilt(NPC, perVelocity: 0.005f, maxTilt: 0.03f); // heavy press barely tilts
	}

	private bool IsSlamDescentActive()
	{
		if ((int)NPC.ai[0] != S_Slam) return false;
		int t = (int)NPC.ai[1];
		return t >= SlamWindupTicks && t <= SlamReturnTick;
	}

	// ----- background producers (always-on hazard layer) --------------------

	private void TickBackground(Player player, bool phase2)
	{
		if (Main.netMode == NetmodeID.MultiplayerClient) return;

		// Crush Zone background producer.
		_crushZoneTimer -= 1f;
		if (_crushZoneTimer <= 0f)
		{
			_crushZoneTimer = phase2 ? CrushZoneIntervalP2 : CrushZoneIntervalP1;
			int maxLive = phase2 ? CrushZoneMaxLiveP2 : CrushZoneMaxLiveP1;
			if (CountLive<CrushZoneProjectile>() < maxLive)
				ImplosionPressAttacks.SpawnCrushZone(NPC, player, CrushZoneDamage, phase2);
		}

		// Carbon Flak background producer (lob arcs of hazards toward random
		// spots near the player). Respects FIFO cap on live carbon hazards.
		_carbonFlakTimer -= 1f;
		if (_carbonFlakTimer <= 0f)
		{
			_carbonFlakTimer = phase2 ? CarbonFlakIntervalP2 : CarbonFlakIntervalP1;
			if (CountLive<CarbonBlockHazard>() < CarbonBlockMaxLive)
				ImplosionPressAttacks.SpawnCarbonFlakBurst(NPC, player, CarbonFlakDamage, phase2);
		}

		// Carbon Storm phase-2 ticker.
		if (phase2)
		{
			if (_carbonStormActive > 0)
			{
				_carbonStormActive--;
				if (_carbonStormActive % ImplosionPressAttacks.CarbonStormInterval == 0)
					ImplosionPressAttacks.SpawnCarbonStormShard(NPC, player, CarbonStormDamage);
			}
			else
			{
				_carbonStormTimer -= 1f;
				if (_carbonStormTimer <= 0f)
				{
					_carbonStormTimer = CarbonStormPeriod;
					_carbonStormActive = CarbonStormDuration;
				}
			}
		}

		// Diamond Forge ticker (phase 2 only) - re-fires the signature attack
		// periodically. Interrupts whatever named attack is active by forcing the
		// state to S_DiamondForge.
		if (phase2 && (int)NPC.ai[0] != S_DiamondForge)
		{
			_diamondForgeTimer -= 1f;
			if (_diamondForgeTimer <= 0f)
			{
				_diamondForgeTimer = DiamondForgePeriod;
				ForceState(S_DiamondForge);
			}
		}
	}

	private int CountLive<T>() where T : ModProjectile
	{
		int type = ModContent.ProjectileType<T>();
		int n = 0;
		for (int i = 0; i < Main.maxProjectiles; i++)
			if (Main.projectile[i].active && Main.projectile[i].type == type) n++;
		return n;
	}

	// ----- state handlers ---------------------------------------------------

	// Glide toward a new anchor (slow heavy-press inertia). When arrived OR
	// after RepositionGlideTicks, pick next attack.
	private void DoReposition(Player player)
	{
		NPC.ai[1]++;
		// Drift the anchor angle slightly each reposition.
		Vector2 anchor = player.Center + new Vector2(
			MathF.Cos(_anchorAngle), MathF.Sin(_anchorAngle)) * StandoffRadius;
		BossAI.MoveToward(NPC, anchor, GlideSpeed, GlideAccel, easeRadius: 180f);

		NPC.localAI[1] = 1f;

		if (NPC.ai[1] >= RepositionGlideTicks)
		{
			if (Main.netMode != NetmodeID.MultiplayerClient)
			{
				NPC.ai[0] = PickAttack(NPC.ai[3] >= 1f, (int)NPC.ai[2]);
				NPC.ai[2] = NPC.ai[0];
				NPC.ai[1] = 0f;
				PushAttackHistory((int)NPC.ai[0]);
				NPC.netUpdate = true;
			}
		}
	}

	private void PushAttackHistory(int attack)
	{
		// FIFO shift: drop oldest, append newest at end.
		for (int i = 0; i < _attackHistory.Length - 1; i++)
			_attackHistory[i] = _attackHistory[i + 1];
		_attackHistory[_attackHistory.Length - 1] = attack;
		if (_attackHistoryCount < _attackHistory.Length) _attackHistoryCount++;
	}

	public string CurrentAttackLabel() => StateName((int)NPC.ai[0]);
	public int CurrentPhase() => (int)NPC.ai[3];

	private static string StateName(int s) => s switch
	{
		S_Reposition    => "Reposition",
		S_Hellblast     => "HellblastVolley",
		S_Mortar        => "MortarSalvo",
		S_Fuse          => "FuseLineCascade",
		S_PressurePulse => "PressurePulse",
		S_Column        => "CompressionColumn",
		S_Tether        => "ImplosionTether",
		S_Slam          => "DetonatorPress",
		S_DiamondForge  => "DiamondForge",
		S_ChainReaction => "ChainReaction",
		_ => $"?({s})",
	};

	private static int DurationFor(int s) => s switch
	{
		S_Reposition    => RepositionGlideTicks,
		S_Hellblast     => Dur_Hellblast,
		S_Mortar        => Dur_Mortar,
		S_Fuse          => Dur_Fuse,
		S_PressurePulse => Dur_PressurePulse,
		S_Column        => Dur_Column,
		S_Tether        => Dur_Tether,
		S_Slam          => Dur_Slam,
		S_DiamondForge  => Dur_DiamondForge,
		S_ChainReaction => Dur_ChainReaction,
		_ => 0,
	};

	// IDebuggableBoss gizmos: anchor crosshair + line from boss to anchor,
	// plus state-specific extras (slam landing target during S_Slam, marker
	// during S_Tether is drawn by the tether projectile itself).
	public void DrawDebugGizmos(Microsoft.Xna.Framework.Graphics.SpriteBatch sb, Vector2 screenPos)
	{
		Player player = Main.player[NPC.target];
		if (player is null || !player.active) return;

		// Anchor = current target hover point. Crosshair only - the long line
		// from boss -> anchor was a screen-spanning rectangle. Numeric values
		// (StandoffRadius, _anchorAngle) live in the text panel.
		Vector2 anchor = player.Center + new Vector2(
			MathF.Cos(_anchorAngle), MathF.Sin(_anchorAngle)) * StandoffRadius;
		DebugOverlaySystem.DrawCrosshair(sb, anchor, screenPos,
			new Color(120, 220, 255), 10f, 1);

		// State-specific extras.
		int state = (int)NPC.ai[0];
		if (state == S_Slam && (int)NPC.ai[1] >= SlamWindupTicks && (int)NPC.ai[1] < SlamLandTick)
		{
			Vector2 slamDest = new(player.Center.X, player.Center.Y + 80f);
			DebugOverlaySystem.DrawCrosshair(sb, slamDest, screenPos,
				new Color(255, 80, 60), 14f, 1);
		}
	}

	// IDebuggableBoss implementation. Called every draw frame while
	// GTConfig.DebugMobs is on; KEEP IT CHEAP - everything here just reads
	// already-tracked runtime state.
	public void BuildDebugLines(List<string> lines)
	{
		bool phase2 = NPC.ai[3] >= 1f;
		int state = (int)NPC.ai[0];
		int t = (int)NPC.ai[1];
		int dur = DurationFor(state);
		float hpPct = NPC.lifeMax > 0 ? 100f * NPC.life / NPC.lifeMax : 0f;
		float angDeg = MathHelper.ToDegrees(_anchorAngle);

		lines.Add($"Implosion Press  [{(phase2 ? "PHASE 2" : "PHASE 1")}]  HP {NPC.life}/{NPC.lifeMax} ({hpPct:0.0}%)");
		lines.Add($"State: {StateName(state)}   t={t}/{dur}  ({(dur > 0 ? 100f * t / dur : 0):0}%)");
		lines.Add($"Anchor: {angDeg:0}deg    Pos: ({(int)NPC.Center.X}, {(int)NPC.Center.Y})");
		lines.Add($"BG  CrushZone in {(int)_crushZoneTimer}t  *  CarbonFlak in {(int)_carbonFlakTimer}t");
		if (phase2)
		{
			string storm = _carbonStormActive > 0
				? $"ACTIVE {_carbonStormActive}t left"
				: $"idle, next in {(int)_carbonStormTimer}t";
			lines.Add($"BG  CarbonStorm: {storm}");
			lines.Add($"BG  DiamondForge in {(int)_diamondForgeTimer}t");
		}

		// Live projectile counts - lets you spot density issues at a glance.
		int crushLive = CountLive<CrushZoneProjectile>();
		int carbonLive = CountLive<CarbonBlockHazard>();
		int shardLive = CountLive<CarbonShardProjectile>();
		int hellLive  = CountLive<HellblastOrbProjectile>();
		int mortarLive = CountLive<MortarShellProjectile>();
		int ringLive  = CountLive<PressureRingProjectile>() + CountLive<DetonatorShockwaveProjectile>() + CountLive<DiamondForgeProjectile>();
		int colLive   = CountLive<CompressionColumnProjectile>();
		int fuseLive  = CountLive<FuseChargeProjectile>();
		lines.Add($"Live  Crush={crushLive}  Carbon={carbonLive}  Shard={shardLive}  Hell={hellLive}  Mort={mortarLive}");
		lines.Add($"Live  Ring={ringLive}  Col={colLive}  Fuse={fuseLive}");

		// Recent attack history (oldest -> newest), so you can spot
		// "it picked Slam three times in a row" patterns.
		if (_attackHistoryCount > 0)
		{
			var hist = new System.Text.StringBuilder("History: ");
			int start = _attackHistory.Length - _attackHistoryCount;
			for (int i = start; i < _attackHistory.Length; i++)
			{
				if (i > start) hist.Append(" -> ");
				hist.Append(StateName(_attackHistory[i]));
			}
			lines.Add(hist.ToString());
		}
	}

	private int PickAttack(bool phase2, int last)
	{
		// Phase 1 pool: 7 named attacks.
		// Phase 2 pool: + ChainReaction. (DiamondForge is force-fired, not in pool.)
		ReadOnlySpan<int> pool = phase2
			? stackalloc int[] { S_Hellblast, S_Mortar, S_Fuse, S_PressurePulse, S_Column, S_Tether, S_Slam, S_ChainReaction }
			: stackalloc int[] { S_Hellblast, S_Mortar, S_Fuse, S_PressurePulse, S_Column, S_Tether, S_Slam };
		int pick = pool[Main.rand.Next(pool.Length)];
		if (pick == last) pick = pool[Main.rand.Next(pool.Length)]; // one reroll
		return pick;
	}

	private void ReturnToReposition()
	{
		// Drift the anchor angle by ~60deg for the next reposition so the boss
		// moves visibly between attacks.
		_anchorAngle += Main.rand.NextFloat(-1.0f, 1.0f);
		// Bias the anchor to remain in the upper hemisphere (boss above player).
		_anchorAngle = MathHelper.Clamp(_anchorAngle, -MathHelper.Pi + 0.4f, -0.4f);
		NPC.ai[0] = S_Reposition;
		NPC.ai[1] = 0f;
		NPC.netUpdate = true;
	}

	// ---- 1: HELLBLAST VOLLEY -----------------------------------------------
	private void RunHellblast(Player player, bool phase2)
	{
		NPC.ai[1]++;
		HoldAtAnchor(player);
		int t = (int)NPC.ai[1];

		if (Main.netMode != NetmodeID.MultiplayerClient)
		{
			// 3 volleys at HellblastVolleyInterval spacing, starting at t=0.
			if (t % ImplosionPressAttacks.HellblastVolleyInterval == 0
			    && t / ImplosionPressAttacks.HellblastVolleyInterval < ImplosionPressAttacks.HellblastVolleyCount)
			{
				ImplosionPressAttacks.SpawnHellblastVolley(NPC, player, HellblastDamage, phase2);
				FlashController(0.5f);
			}
		}

		if (NPC.ai[1] >= Dur_Hellblast) ReturnToReposition();
	}

	// ---- 2: MORTAR SALVO ---------------------------------------------------
	private void RunMortar(Player player, bool phase2)
	{
		NPC.ai[1]++;
		HoldAtAnchor(player);
		int t = (int)NPC.ai[1];

		if (Main.netMode != NetmodeID.MultiplayerClient)
		{
			if (t % ImplosionPressAttacks.MortarSalvoInterval == 0
			    && t / ImplosionPressAttacks.MortarSalvoInterval < ImplosionPressAttacks.MortarSalvoCount)
			{
				int idx = t / ImplosionPressAttacks.MortarSalvoInterval;
				ImplosionPressAttacks.SpawnMortarShell(NPC, player, MortarDamage, phase2, idx);
				FlashController(0.4f);
			}
		}

		if (NPC.ai[1] >= Dur_Mortar) ReturnToReposition();
	}

	// ---- 3: FUSE-LINE CASCADE ----------------------------------------------
	private void RunFuse(Player player, bool phase2)
	{
		NPC.ai[1]++;
		HoldAtAnchor(player);

		// Single-tick spawn at t=20 (after a short telegraph hold).
		if (NPC.ai[1] == 20f && Main.netMode != NetmodeID.MultiplayerClient)
		{
			ImplosionPressAttacks.SpawnFuseLine(NPC, player, FuseChargeDamage, phase2);
			FlashController(0.7f);
		}

		if (NPC.ai[1] >= Dur_Fuse) ReturnToReposition();
	}

	// ---- 4: PRESSURE PULSE -------------------------------------------------
	private void RunPressurePulse(Player player, bool phase2)
	{
		NPC.ai[1]++;
		HoldAtAnchor(player);

		// Charge up: brighter glow for 30 ticks, then fire.
		if (NPC.ai[1] < 30f) FlashController(0.3f);

		if (NPC.ai[1] == 30f && Main.netMode != NetmodeID.MultiplayerClient)
		{
			ImplosionPressAttacks.SpawnPressurePulse(NPC, PressureRingDamage);
			FlashController(1.2f);
		}

		if (NPC.ai[1] >= Dur_PressurePulse) ReturnToReposition();
	}

	// ---- 5: COMPRESSION COLUMN --------------------------------------------
	// Three columns; boss repositions 2 tiles between each.
	private void RunColumn(Player player, bool phase2)
	{
		NPC.ai[1]++;
		HoldAtAnchor(player, easeRadius: 60f);
		int t = (int)NPC.ai[1];

		const int columnSpacing = 90; // ticks between column spawns
		const int columnsToFire = 3;

		if (Main.netMode != NetmodeID.MultiplayerClient && t % columnSpacing == 0
		    && t / columnSpacing < columnsToFire)
		{
			ImplosionPressAttacks.SpawnCompressionColumn(NPC, player, ColumnDamage);
			FlashController(0.6f);
			// Nudge anchor 60 px sideways for the next column.
			_anchorAngle += 0.18f * (Main.rand.NextBool() ? 1f : -1f);
		}

		if (NPC.ai[1] >= Dur_Column) ReturnToReposition();
	}

	// ---- 6: IMPLOSION TETHER ----------------------------------------------
	private void RunTether(Player player, bool phase2)
	{
		NPC.ai[1]++;
		HoldAtAnchor(player);

		if (NPC.ai[1] == 1f && Main.netMode != NetmodeID.MultiplayerClient)
		{
			ImplosionPressAttacks.SpawnTether(NPC, player, TetherDamage, phase2);
			FlashController(0.5f);
		}

		if (NPC.ai[1] >= Dur_Tether) ReturnToReposition();
	}

	// ---- 7: DETONATOR PRESS (SLAM) ----------------------------------------
	// Boss rapid-drops to ground, releases triple shockwave, lifts back.
	private void RunSlam(Player player, bool phase2)
	{
		NPC.ai[1]++;
		int t = (int)NPC.ai[1];

		if (t == 0)
		{
			_preSlamAnchor = NPC.Center;
		}
		else if (t < SlamWindupTicks)
		{
			// Windup: anchor in place + charge glow.
			HoldAtAnchor(player, easeRadius: 60f);
			FlashController(0.6f);
		}
		else if (t < SlamLandTick)
		{
			// Descend: aim for player.Center.Y, snap downward.
			float dropY = player.Center.Y + 80f;
			Vector2 dest = new(player.Center.X, dropY);
			BossAI.MoveToward(NPC, dest, 28f, 0.5f, easeRadius: 80f);
		}
		else if (t == SlamLandTick)
		{
			// Impact!
			NPC.velocity = Vector2.Zero;
			if (Main.netMode != NetmodeID.MultiplayerClient)
				ImplosionPressAttacks.SpawnDetonatorImpact(NPC, SlamDamage, phase2);
			FlashController(1.5f);
		}
		else if (t < SlamReturnTick)
		{
			// Pause + smoke.
			NPC.velocity *= 0.6f;
			if (!Main.dedServ && Main.rand.NextBool(2))
			{
				var d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height,
					DustID.Smoke, 0f, -2f, 100, default, 1.6f);
				d.noGravity = true;
			}
		}
		else
		{
			// Lift back to pre-slam anchor.
			BossAI.MoveToward(NPC, _preSlamAnchor, 8f, 0.1f, easeRadius: 100f);
		}

		if (NPC.ai[1] >= Dur_Slam) ReturnToReposition();
	}

	// ---- 8: DIAMOND FORGE (phase 2 signature) -----------------------------
	private void RunDiamondForge(Player player, bool phase2)
	{
		NPC.ai[1]++;
		// Boss anchors at arena centre during the forge.
		HoldAtAnchor(player, easeRadius: 30f);
		FlashController(0.9f);

		if (NPC.ai[1] == 30f && Main.netMode != NetmodeID.MultiplayerClient)
		{
			ImplosionPressAttacks.SpawnDiamondForge(NPC, player, DiamondForgeDamage);
		}

		if (NPC.ai[1] >= Dur_DiamondForge) ReturnToReposition();
	}

	// ---- 9: CHAIN REACTION (phase 2) --------------------------------------
	private void RunChainReaction(Player player, bool phase2)
	{
		NPC.ai[1]++;
		HoldAtAnchor(player);

		if (NPC.ai[1] == 30f && Main.netMode != NetmodeID.MultiplayerClient)
		{
			ImplosionPressAttacks.SpawnChainReaction(NPC, player, CrushZoneDamage, phase2);
			FlashController(0.7f);
		}

		if (NPC.ai[1] >= Dur_ChainReaction) ReturnToReposition();
	}

	// ----- transitions -----------------------------------------------------

	private void EnterPhase2(Player player)
	{
		NPC.ai[3] = 1f;
		SoundEngine.PlaySound(SoundID.Item62 with { Pitch = -0.5f, Volume = 1.2f }, NPC.Center);
		NPC.localAI[3] = 1.2f; // big glow flash

		if (!Main.dedServ)
			for (int i = 0; i < 32; i++)
			{
				var d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height,
					DustID.Torch, 0f, 0f, 100, default, 1.7f);
				d.noGravity = true;
				d.velocity *= 2.5f;
			}

		// One-time nova ring on phase enter + immediate Carbon Storm + reset timers.
		if (Main.netMode != NetmodeID.MultiplayerClient)
		{
			ImplosionPressAttacks.SpawnPressurePulse(NPC, PressureRingDamage);
			_carbonStormActive = CarbonStormDuration;
			_diamondForgeTimer = DiamondForgePeriod * 0.4f; // first forge ~10s in
		}
	}

	private void ForceState(int state)
	{
		NPC.ai[0] = state;
		NPC.ai[1] = 0f;
		NPC.netUpdate = true;
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

	// ----- helpers ---------------------------------------------------------

	private void HoldAtAnchor(Player player, float easeRadius = 140f)
	{
		Vector2 anchor = player.Center + new Vector2(
			MathF.Cos(_anchorAngle), MathF.Sin(_anchorAngle)) * StandoffRadius;
		BossAI.MoveToward(NPC, anchor, GlideSpeed * 0.55f, GlideAccel * 0.7f, easeRadius);
	}

	private void FlashController(float boost)
	{
		NPC.localAI[3] = Math.Max(NPC.localAI[3], boost);
	}

	// ---- visuals ----------------------------------------------------------

	private void UpdateVisuals()
	{
		NPC.localAI[0]++;
		bool phase2 = NPC.ai[3] >= 1f;

		// Controller-face glow pulse, brighter (and whiter) in phase 2.
		float baseG = phase2 ? 0.80f : 0.50f;
		float pulse = baseG + 0.10f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 2.5f);
		if (NPC.localAI[3] > 0f)
		{
			pulse += NPC.localAI[3];
			NPC.localAI[3] *= 0.88f;
			if (NPC.localAI[3] < 0.02f) NPC.localAI[3] = 0f;
		}
		NPC.localAI[2] = MathHelper.Clamp(pulse, 0f, 1.8f);

		// Constant muffler smoke from top-centre.
		if (!Main.dedServ && Main.rand.NextBool(phase2 ? 1 : 2))
			EmitMufflerSmoke(1);
	}

	private void EmitMufflerSmoke(int count)
	{
		if (Main.dedServ) return;
		Vector2 vent = NPC.Center + new Vector2(0f, -ImplosionPressRenderer.Height * 0.42f * NPC.scale);
		bool phase2 = NPC.ai[3] >= 1f;
		var col = phase2 ? new Color(220, 100, 80) : new Color(120, 115, 110);
		for (int i = 0; i < count; i++)
		{
			Vector2 at = vent + new Vector2(Main.rand.Next(-14, 15) * NPC.scale, Main.rand.Next(-4, 5));
			Vector2 vel = new(Main.rand.NextFloat(-0.3f, 0.3f), -Main.rand.NextFloat(0.8f, 1.6f));
			var d = Dust.NewDustPerfect(at, DustID.Smoke, vel, 90, col, Main.rand.NextFloat(1.3f, 1.9f));
			d.noGravity = true;
			d.fadeIn = 0.4f;
		}
	}

	public override void HitEffect(NPC.HitInfo hit)
	{
		int n = NPC.life <= 0 ? 40 : 5;
		for (int i = 0; i < n; i++)
		{
			var d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height,
				DustID.Torch, hit.HitDirection, -1f, 100, default, 1.3f);
			d.noGravity = true;
		}
	}

	public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
	{
		BossHeadHelper.SwapBakedHead(NPC, ImplosionPressRenderer.BossHeadAsset, ref _headSwapped);

		var body = ImplosionPressRenderer.Body;
		if (body is null) return true; // composite failed -> placeholder sprite

		Vector2 pos = NPC.Center - screenPos;
		float scale = NPC.scale;
		var origin = new Vector2(body.Width / 2f, body.Height / 2f);

		spriteBatch.Draw(body, pos, null, drawColor, NPC.rotation, origin, scale, SpriteEffects.None, 0f);

		var glow = ImplosionPressRenderer.Glow;
		if (glow is not null)
		{
			bool phase2 = NPC.ai[3] >= 1f;
			// Hue-shift the emissive controller face by phase: warm orange -> white-hot.
			Color hue = phase2 ? new Color(255, 230, 200) : new Color(255, 140, 90);
			float g = NPC.localAI[2];
			spriteBatch.Draw(glow, pos, null, hue * MathHelper.Clamp(g, 0f, 1f),
				NPC.rotation, origin, scale, SpriteEffects.None, 0f);
			if (g > 1f) // telegraph / phase 2 / slam: extra bright pass
				spriteBatch.Draw(glow, pos, null, hue * (g - 1f),
					NPC.rotation, origin, scale, SpriteEffects.None, 0f);
		}

		return false;
	}
}

// Type-existence helper so Dur_Tether can read a const at compile time without
// forward-referencing the projectile class member directly (the SustainTicks
// const is on ImplosionTetherProjectile, in the Projectiles namespace).
internal static class ImplosionPressTether
{
	public const int SustainTicks = ImplosionTetherProjectile.SustainTicks;
}
