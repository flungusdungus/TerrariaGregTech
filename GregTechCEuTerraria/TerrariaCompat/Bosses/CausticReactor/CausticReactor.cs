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

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.CausticReactor;

// The Caustic Reactor - the EV-age custom boss, slotted post-Plantera (the
// corrosive/toxic counterpart to the Fallen EBF's heat and the Vacuum Freezer's
// cold). A floating 3x3 chemical-reactor chamber (inert PTFE casing + reactor
// controller face, see CausticReactorRenderer) that does NOT chase or fly: it's
// an ANCHORED CASTER in the Empress-of-Light / Touhou mould. It teleports
// between arena positions and paints rich geometric bullet-hell patterns - rose
// curves, golden-angle sunflowers, Lissajous figures, hex pulses, twin spirals,
// a cardioid finale (see CausticReactorAttacks). The patterns ARE the fight; the
// body just emits.
//
// State is held entirely in ai[0..3] (synced) + localAI[0..3] (client visuals),
// so no SendExtraAI is needed.
//   ai[0] = state   ai[1] = state timer
//   ai[2] = last spell (avoid repeats)   ai[3] = phase (0 = corrosive, 1 = distilled)
[AutoloadBossHead]
public class CausticReactor : ModNPC, IDebuggableBoss
{
	// State machine. 0 = reposition (teleport between casts); 1..6 = spell cards.
	private const int S_Reposition = 0, S_Rose = 1, S_Phyllo = 2, S_Lissajous = 3,
		S_Hex = 4, S_Spiral = 5, S_Cardioid = 6;

	// ---- tunables ----------------------------------------------------------
	private const int DropletDamage = 32;
	private const float StandoffRadius = 360f; // how far from the player it anchors
	private const int FadeOutTicks = 16;        // reposition: dissolve out
	private const int FadeInTicks = 16;         // reposition: rematerialise
	private const int TelegraphTicks = 24;      // pre-cast glow tell before bullets
	private const int SpellDurP1 = 240;
	private const int SpellDurP2 = 210;

	// Lateral orbit drift while casting - the boss is a caster (no dashing) but
	// shouldn't be a statue. It glides slowly around the player at standoff
	// distance, reversing direction per card. Phase 2 drifts a touch faster.
	private const float OrbitRate = 0.013f;  // radians/tick the anchor sweeps
	private const float OrbitSpeed = 3.6f;   // max glide speed toward the moving anchor
	private const float OrbitAccel = 0.04f;

	// Ambient chaos: a steady drip of randomly-aimed droplets fired DURING every
	// spell, independent of the card's geometry, so there's never a safe lull
	// between patterns. Loosely player-aimed (wide spread + random speed) = chaos,
	// not a tracking laser. Server-authoritative; tune freely.
	private const int ChaosIntervalP1 = 20;   // ticks between bursts (~3/s)
	private const int ChaosIntervalP2 = 13;   // phase 2 (~4.6/s)
	private const float ChaosSpreadDeg = 75f; // half-cone around the player direction

	private static bool _headSwapped;

	// Cached emitter callback - a `this.Emit` method group converts to a fresh
	// delegate on every use, which would allocate each tick in the hot spell loop.
	// Build it once per entity instead.
	private CausticReactorAttacks.Emit? _emit;
	private CausticReactorAttacks.Emit EmitCallback => _emit ??= SpawnDroplet;

	public override string Texture => "GregTechCEuTerraria/Content/Textures/block/casings/solid/machine_casing_inert_ptfe";
	public override string BossHeadTexture => "GregTechCEuTerraria/Content/Textures/block/casings/solid/machine_casing_inert_ptfe";

	public override void SetStaticDefaults()
	{
		Main.npcFrameCount[Type] = 1;
		NPCID.Sets.MPAllowedEnemies[Type] = true;
		NPCID.Sets.BossBestiaryPriority.Add(Type);

		// A vat of acid shrugs off poison/venom.
		NPCID.Sets.SpecificDebuffImmunity[Type] ??= new bool?[BuffLoader.BuffCount];
		NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.Poisoned] = true;
		NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.Venom] = true;
		NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.Confused] = true;

		Language.GetOrRegister("Mods.GregTechCEuTerraria.NPCs.CausticReactor.DisplayName", () => "Caustic Reactor");
		Language.GetOrRegister("Mods.GregTechCEuTerraria.NPCs.CausticReactor.Bestiary",
			() => "A sealed chemical reactor that broke containment and now hangs in the air, venting its reagents in blooming geometric sprays of acid. It doesn't give chase - it anchors, paints the arena with corrosive light, and dares you to find the gaps.");
	}

	public override void SetDefaults()
	{
		NPC.scale = 2f;
		// Sprite is drawn at (renderer size * scale) in PreDraw, INDEPENDENT of
		// width/height; inset the hitbox to ~70% so contact damage sits under the
		// chamber core, both centred on NPC.Center (same convention as the others).
		NPC.width = (int)(CausticReactorRenderer.Width * NPC.scale * 0.70f);
		NPC.height = (int)(CausticReactorRenderer.Height * NPC.scale * 0.70f);
		NPC.damage = 40;
		NPC.defense = 22;
		NPC.lifeMax = 14000;
		NPC.HitSound = SoundID.NPCHit4;       // metallic clang
		NPC.DeathSound = SoundID.NPCDeath14;  // explosion
		NPC.knockBackResist = 0f;
		NPC.noGravity = true;
		NPC.noTileCollide = true;
		NPC.boss = true;
		NPC.npcSlots = 10f;
		NPC.aiStyle = -1;
		NPC.value = Item.buyPrice(gold: 8);
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
		bestiaryEntry.Info.Add(new FlavorTextBestiaryInfoElement("Mods.GregTechCEuTerraria.NPCs.CausticReactor.Bestiary"));
	}

	public override void ModifyNPCLoot(NPCLoot npcLoot)
	{
		// Signature reward: 15 chemically-inert machine casings - enough to start
		// your first Large Chemical Reactor multiblock. Always drops.
		if (Mod.TryFind<ModItem>("inert_machine_casing", out var casing))
			npcLoot.Add(ItemDropRule.Common(casing.Type, 1, 15, 15));

		// Plus EV-tier "age loot" (tier 4 + components), gated by EnableBossDrops.
		var condition = new BossDrops.BossDropCondition();
		foreach (var d in BossDrops.BossDropRegistry.GetTierDrops(4, withComponents: true))
			npcLoot.Add(new ItemDropWithConditionRule(d.ItemType, chanceDenominator: 1,
				amountDroppedMinimum: d.Min, amountDroppedMaximum: d.Max, condition));
	}

	public override void OnKill()
	{
		if (Main.netMode != NetmodeID.MultiplayerClient)
			CausticReactorWorld.MarkDowned();
	}

	// ---- AI ----------------------------------------------------------------

	public override void AI()
	{
		UpdateVisuals();

		float glow = NPC.localAI[2];
		Lighting.AddLight(NPC.Center, 0.30f * glow, 0.52f * glow, 0.16f * glow);

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
			case S_Reposition: Reposition(player, phase2); break;
			case S_Rose:       RunSpell(player, phase2, S_Rose);      break;
			case S_Phyllo:     RunSpell(player, phase2, S_Phyllo);    break;
			case S_Lissajous:  RunSpell(player, phase2, S_Lissajous); break;
			case S_Hex:        RunSpell(player, phase2, S_Hex);       break;
			case S_Spiral:     RunSpell(player, phase2, S_Spiral);    break;
			case S_Cardioid:   RunSpell(player, phase2, S_Cardioid);  break;
		}

		// Background chaos layered on top of whatever card is active (skip only
		// while teleporting - the body is dissolved then).
		if ((int)NPC.ai[0] != S_Reposition)
			AmbientChaos(player, phase2);

		BossAI.SmoothTilt(NPC, perVelocity: 0.010f, maxTilt: 0.06f); // heavy chamber, barely tilts
	}

	// Teleport between arena anchor points with a dissolve-out / rematerialise.
	// Server snap-sets the new position at the fade midpoint (NPC position is
	// auto-synced on netUpdate, so no anchor needs storing in ai[]); clients just
	// render the dust fade off the synced timer.
	private void Reposition(Player player, bool phase2)
	{
		NPC.velocity *= 0.85f;
		NPC.ai[1]++;
		int t = (int)NPC.ai[1];

		// Fade alpha for the draw (1 -> 0 -> 1).
		if (t <= FadeOutTicks)
			NPC.localAI[1] = 1f - t / (float)FadeOutTicks;
		else
			NPC.localAI[1] = MathHelper.Clamp((t - FadeOutTicks) / (float)FadeInTicks, 0f, 1f);

		EmitDissolveDust();

		// Teleport once at the fade midpoint.
		if (t == FadeOutTicks)
		{
			SoundEngine.PlaySound(SoundID.Item8 with { Pitch = -0.4f }, NPC.Center); // hiss (all clients)
			if (Main.netMode != NetmodeID.MultiplayerClient)
			{
				float ang = -MathHelper.PiOver2 + Main.rand.NextFloat(-1.05f, 1.05f); // upper arc
				NPC.Center = player.Center + ang.ToRotationVector2() * StandoffRadius;
				NPC.velocity = Vector2.Zero;
				NPC.netUpdate = true;
			}
		}

		// Pick the next spell once the rematerialise completes (server-authoritative).
		if (t >= FadeOutTicks + FadeInTicks)
		{
			NPC.localAI[1] = 1f;
			if (Main.netMode != NetmodeID.MultiplayerClient)
			{
				NPC.ai[0] = PickSpell(phase2, (int)NPC.ai[2]);
				NPC.ai[2] = NPC.ai[0];
				NPC.ai[1] = 0f;
				NPC.netUpdate = true;
			}
		}
	}

	private int PickSpell(bool phase2, int last)
	{
		// Phase 1 excludes the cardioid (it's the phase-2 opener / finale).
		ReadOnlySpan<int> pool = phase2
			? stackalloc int[] { S_Rose, S_Phyllo, S_Lissajous, S_Hex, S_Spiral, S_Cardioid }
			: stackalloc int[] { S_Rose, S_Phyllo, S_Lissajous, S_Hex, S_Spiral };
		int pick = pool[Main.rand.Next(pool.Length)];
		if (pick == last) pick = pool[Main.rand.Next(pool.Length)]; // one reroll
		return pick;
	}

	// Shared spell driver: hold near the anchor, telegraph, then run the geometric
	// emitter every tick until the duration elapses, then return to reposition.
	private void RunSpell(Player player, bool phase2, int spell)
	{
		// Slow lateral orbit around the player at standoff distance - moving, but
		// still a caster (no dashing). Direction alternates per card (ai[2]).
		// Derived from the live position (synced) + player center, eased via
		// MoveToward, so it's MP-stable like the Vacuum Freezer's drift.
		float orbitDir = ((int)NPC.ai[2] & 1) == 0 ? 1f : -1f;
		Vector2 toBoss = NPC.Center - player.Center;
		if (toBoss == Vector2.Zero) toBoss = -Vector2.UnitY;
		float ang = toBoss.ToRotation() + orbitDir * OrbitRate * (phase2 ? 1.3f : 1f);
		Vector2 anchor = player.Center + ang.ToRotationVector2() * StandoffRadius;
		BossAI.MoveToward(NPC, anchor, OrbitSpeed, OrbitAccel, easeRadius: 140f);

		NPC.localAI[1] = 1f;
		NPC.ai[1]++;

		int spellDur = phase2 ? SpellDurP2 : SpellDurP1;
		_activePalette = PaletteFor(spell, phase2);

		if (NPC.ai[1] < TelegraphTicks)
		{
			Telegraph();
			return;
		}

		int t = (int)NPC.ai[1] - TelegraphTicks;

		// Periodic cast hiss (all clients), keyed off the synced timer.
		if (t % 18 == 0)
			SoundEngine.PlaySound(SoundID.Item85 with { Volume = 0.5f, Pitch = 0.3f }, NPC.Center);

		// Emitters spawn projectiles -> server only.
		if (Main.netMode != NetmodeID.MultiplayerClient)
		{
			var emit = EmitCallback;
			switch (spell)
			{
				case S_Rose:      CausticReactorAttacks.Rose(NPC, t, phase2, spellDur, emit); break;
				case S_Phyllo:    CausticReactorAttacks.Phyllotaxis(NPC, t, phase2, emit); break;
				case S_Lissajous: CausticReactorAttacks.Lissajous(NPC, t, phase2, emit); break;
				case S_Hex:       CausticReactorAttacks.Hex(NPC, t, phase2, emit); break;
				case S_Spiral:    CausticReactorAttacks.Spiral(NPC, t, phase2, emit); break;
				case S_Cardioid:
					CausticReactorAttacks.Cardioid(NPC, t, phase2, emit);
					if (t == 0) SpawnCorrosivePools(player); // ground hazards, once
					break;
			}
		}

		if (NPC.ai[1] >= TelegraphTicks + spellDur)
			ReturnToReposition();
	}

	// Card-independent background pressure - see the Chaos* tunables. Fires on a
	// fixed cadence regardless of the active spell; loosely aims at the player
	// with a wide random cone + random speed so it adds noise without becoming a
	// homing threat. Phase 2 fires faster and occasionally doubles up.
	private void AmbientChaos(Player player, bool phase2)
	{
		if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return;
		int interval = phase2 ? ChaosIntervalP2 : ChaosIntervalP1;
		if (Main.GameUpdateCount % (uint)interval != 0) return;

		_activePalette = phase2 ? 7 : 0; // neutral chaos hue, distinct from cards
		int count = phase2 && Main.rand.NextBool(2) ? 2 : 1;
		Vector2 baseDir = (player.Center - NPC.Center).SafeNormalize(Vector2.UnitY);
		float spread = MathHelper.ToRadians(ChaosSpreadDeg);
		for (int i = 0; i < count; i++)
		{
			float ang = Main.rand.NextFloat(-spread / 2f, spread / 2f);
			float speed = Main.rand.NextFloat(2.6f, 4.8f) * (phase2 ? 1.15f : 1f);
			SpawnDroplet(NPC.Center, baseDir.RotatedBy(ang) * speed);
		}
	}

	private void ReturnToReposition()
	{
		NPC.ai[0] = S_Reposition;
		NPC.ai[1] = 0f;
		NPC.netUpdate = true;
	}

	private void EnterPhase2(Player player)
	{
		NPC.ai[3] = 1f;
		SoundEngine.PlaySound(SoundID.Item62, NPC.Center); // chemical whoosh
		NPC.localAI[3] = 0.9f; // big glow flash

		if (!Main.dedServ)
			for (int i = 0; i < 30; i++)
			{
				var d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.GreenFairy, 0f, 0f, 100, default, 1.6f);
				d.noGravity = true;
				d.velocity *= 2.5f;
			}

		// One-time corrosive nova ring, then snap straight into the cardioid opener.
		if (Main.netMode != NetmodeID.MultiplayerClient)
		{
			_activePalette = 13; // violet cardioid
			const int n = 18;
			for (int i = 0; i < n; i++)
				SpawnDroplet(NPC.Center, (MathHelper.TwoPi * i / n).ToRotationVector2() * 4f);

			NPC.ai[0] = S_Cardioid;
			NPC.ai[1] = 0f;
			NPC.ai[2] = S_Cardioid;
			NPC.netUpdate = true;
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
		S_Reposition => "Reposition", S_Rose => "Rose", S_Phyllo => "Phyllotaxis",
		S_Lissajous => "Lissajous", S_Hex => "Hex", S_Spiral => "Spiral", S_Cardioid => "Cardioid",
		_ => $"?({s})",
	};

	public void BuildDebugLines(System.Collections.Generic.List<string> lines)
	{
		bool phase2 = NPC.ai[3] >= 1f;
		int state = (int)NPC.ai[0];
		int t = (int)NPC.ai[1];
		int dur = state == S_Reposition ? FadeOutTicks + FadeInTicks
			: TelegraphTicks + (phase2 ? SpellDurP2 : SpellDurP1);
		float hpPct = NPC.lifeMax > 0 ? 100f * NPC.life / NPC.lifeMax : 0f;
		Player p = Main.player[NPC.target];
		float dist = p?.active == true ? Vector2.Distance(NPC.Center, p.Center) : 0f;
		lines.Add($"Caustic Reactor  [{(phase2 ? "PHASE 2" : "PHASE 1")}]  HP {NPC.life}/{NPC.lifeMax} ({hpPct:0.0}%)");
		lines.Add($"Spell: {StateName(state)}   t={t}/{dur}");
		lines.Add($"Last: {StateName((int)NPC.ai[2])}   Standoff: {dist:0}/{(int)StandoffRadius}px");
	}

	public void DrawDebugGizmos(Microsoft.Xna.Framework.Graphics.SpriteBatch sb, Vector2 screenPos)
	{
		Player p = Main.player[NPC.target];
		if (p is null || !p.active) return;
		// Crosshair only - boss -> player line was screen-spanning and read as
		// a blocking rectangle. Standoff radius is in the text panel.
		DebugOverlaySystem.DrawCrosshair(sb, p.Center, screenPos,
			new Color(180, 120, 230), 10f, 1);
	}

	// Palette index used by the next SpawnDroplet - set per emit source so each
	// spell card has its own colour and the ambient chaos is its own neutral hue.
	private int _activePalette;

	// Map a spell state -> its palette index (see AcidDropletProjectile._palette).
	// Phase 2 shifts the whole set into the violet "distilled" block (+7).
	private static int PaletteFor(int spell, bool phase2)
	{
		int idx = spell switch
		{
			S_Rose => 1, S_Phyllo => 2, S_Lissajous => 3, S_Hex => 4,
			S_Spiral => 5, S_Cardioid => 6, _ => 0, // 0 = chaos/neutral
		};
		return phase2 ? idx + 7 : idx;
	}

	private void SpawnDroplet(Vector2 pos, Vector2 vel)
	{
		if (Main.netMode == NetmodeID.MultiplayerClient) return;
		Projectile.NewProjectile(NPC.GetSource_FromAI(), pos, vel,
			ModContent.ProjectileType<AcidDropletProjectile>(), DropletDamage, 1.5f, Main.myPlayer,
			ai0: 0f, ai1: _activePalette);
	}

	private void SpawnCorrosivePools(Player player)
	{
		if (Main.netMode == NetmodeID.MultiplayerClient) return;
		ReadOnlySpan<float> offsets = stackalloc float[] { -300f, 0f, 300f };
		foreach (float dx in offsets)
			Projectile.NewProjectile(NPC.GetSource_FromAI(),
				player.Center + new Vector2(dx, 120f), Vector2.Zero,
				ModContent.ProjectileType<CorrosivePoolProjectile>(), DropletDamage, 0f, Main.myPlayer);
	}

	// Reactor-face glow flash + a vent bubble - the "about to cast" tell.
	private void Telegraph()
	{
		NPC.localAI[3] = Math.Max(NPC.localAI[3], 0.55f);
		EmitVentBubbles(2);
	}

	// ---- visuals (run on all clients) --------------------------------------

	private void UpdateVisuals()
	{
		NPC.localAI[0]++;
		bool phase2 = NPC.ai[3] >= 1f;

		// Reactor-face glow pulse, brighter (and violet) when distilled (phase 2),
		// plus a decaying telegraph/phase boost.
		float baseG = phase2 ? 0.85f : 0.55f;
		float pulse = baseG + 0.12f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 3f);
		if (NPC.localAI[3] > 0f)
		{
			pulse += NPC.localAI[3];
			NPC.localAI[3] *= 0.9f;
			if (NPC.localAI[3] < 0.02f) NPC.localAI[3] = 0f;
		}
		NPC.localAI[2] = MathHelper.Clamp(pulse, 0f, 1.5f);

		// Constant acid bubbles drifting up from the chamber vents.
		if (Main.rand.NextBool(phase2 ? 1 : 2))
			EmitVentBubbles(1);
	}

	private void EmitDissolveDust()
	{
		if (Main.dedServ) return;
		// Swirl dust inward toward the core as it dematerialises.
		for (int i = 0; i < 3; i++)
		{
			float ang = Main.rand.NextFloat(MathHelper.TwoPi);
			Vector2 at = NPC.Center + ang.ToRotationVector2() * (CausticReactorRenderer.Width * 0.5f * NPC.scale);
			var d = Dust.NewDustPerfect(at, DustID.GreenFairy, (NPC.Center - at) * 0.04f, 100, default, 1.4f);
			d.noGravity = true;
		}
	}

	private void EmitVentBubbles(int count)
	{
		if (Main.dedServ) return;
		Vector2 vent = NPC.Center + new Vector2(0f, -CausticReactorRenderer.Height * 0.10f * NPC.scale);
		bool phase2 = NPC.ai[3] >= 1f;
		var col = phase2 ? new Color(190, 120, 230) : new Color(160, 215, 70);
		for (int i = 0; i < count; i++)
		{
			Vector2 at = vent + new Vector2(Main.rand.Next(-16, 17) * NPC.scale, Main.rand.Next(-4, 5));
			Vector2 vel = new(Main.rand.NextFloat(-0.4f, 0.4f), -Main.rand.NextFloat(0.5f, 1.4f));
			var d = Dust.NewDustPerfect(at, DustID.GreenFairy, vel, 110, col, Main.rand.NextFloat(1.1f, 1.7f));
			d.noGravity = true;
			d.fadeIn = 0.4f;
		}
	}

	public override void HitEffect(NPC.HitInfo hit)
	{
		int n = NPC.life <= 0 ? 34 : 4;
		for (int i = 0; i < n; i++)
		{
			var d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height,
				DustID.GreenFairy, hit.HitDirection, -1f, 100, default, 1.3f);
			d.noGravity = true;
		}
	}

	public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
	{
		BossHeadHelper.SwapBakedHead(NPC, CausticReactorRenderer.BossHeadAsset, ref _headSwapped);

		var body = CausticReactorRenderer.Body;
		if (body is null) return true; // composite failed -> placeholder sprite

		Vector2 pos = NPC.Center - screenPos;
		float scale = NPC.scale;
		var origin = new Vector2(body.Width / 2f, body.Height / 2f);

		// Reposition fade (localAI[1]: 1 = solid, 0 = dissolved); only the teleport
		// state dissolves, every other state draws solid.
		float fade = NPC.ai[0] == S_Reposition ? MathHelper.Clamp(NPC.localAI[1], 0f, 1f) : 1f;
		Color bodyColor = drawColor * fade;

		spriteBatch.Draw(body, pos, null, bodyColor, NPC.rotation, origin, scale, SpriteEffects.None, 0f);

		var glow = CausticReactorRenderer.Glow;
		if (glow is not null)
		{
			bool phase2 = NPC.ai[3] >= 1f;
			// Hue-shift the emissive reactor face by phase: corrosive green -> violet.
			Color hue = phase2 ? new Color(200, 130, 245) : new Color(170, 230, 90);
			float g = NPC.localAI[2] * fade;
			spriteBatch.Draw(glow, pos, null, hue * MathHelper.Clamp(g, 0f, 1f),
				NPC.rotation, origin, scale, SpriteEffects.None, 0f);
			if (g > 1f) // phase-2 / telegraph: extra bright pass
				spriteBatch.Draw(glow, pos, null, hue * (g - 1f),
					NPC.rotation, origin, scale, SpriteEffects.None, 0f);
		}

		return false;
	}
}
