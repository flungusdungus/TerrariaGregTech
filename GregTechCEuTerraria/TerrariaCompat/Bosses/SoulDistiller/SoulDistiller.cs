#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Bosses.Common;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.Bestiary;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.SoulDistiller;

// The Soul Distiller - a hardmode, pre-mechanical-boss worm shaped like a
// distillation tower bent into a serpent. 1.5x the length of a vanilla
// mechanical worm. Cycles all four attacks (liquid spray / bucket volleys /
// raining oil clouds / tail gas), its body colour-graded by fraction (cool gas
// at the head, heavy crude at the tail). At 50% health it FRACTIONATES: it
// distills itself into four shorter sub-worms (refinery gas / naphtha / light
// oil / heavy oil), each with 25% of max health and its own signature attack.
// Defeat = all four sub-worms dead. Summoned by the Dirty Stainless Steel Casing.
[AutoloadBossHead]
public class SoulDistiller : SoulDistillerHeadBase
{
	private const int FractionSegmentCount = 28;

	private bool _split;

	public override string Texture => "GregTechCEuTerraria/Content/Textures/block/casings/solid/machine_casing_clean_stainless_steel";
	public override string BossHeadTexture => "GregTechCEuTerraria/Content/Textures/block/casings/solid/machine_casing_clean_stainless_steel";

	protected override int BodyType => ModContent.NPCType<SoulDistillerBody>();
	protected override int TailType => ModContent.NPCType<SoulDistillerTail>();
	protected override int SegmentCount => 120; // 1.5x the Destroyer's 80

	// MaxSpeed = 12.8 = 80% of the Destroyer's 16 px/tick combat top speed (the
	// user wants ~20% slower). TurnRate 0.028 carves an even wider arc than the
	// Destroyer (turn radius ~ speed/turnRate ~ 457 px ~ 29 tiles) so the worm
	// commits to a heading and arcs PAST instead of laser-tracking; combined
	// with the aim wobble below it can't tighten onto a strafe-dodging player.
	// The 2x sprite scale (NPC.scale = 1.24) makes the head physically larger;
	// GapDistance bumped to 60 so segments space proportionally to the bigger
	// silhouette. Don't crank TurnRate back up without dropping AimWobbleRadius.
	protected override WormMovementConfig MoveConfig => new()
	{
		MaxSpeed = 12.8f,
		Acceleration = 0.22f,
		TurnRate = 0.028f,
		MinSpeedFrac = 0.6f,
		GapDistance = 60f,
	};

	// Loose aim - the worm targets a point that orbits the player at up to ~320
	// px (~20 tiles) on a slow elliptical sweep. Combined with the wide turn
	// radius this means the worm regularly veers PAST a stationary player; a
	// strafing player breaks tracking almost instantly. Tune via WormAI.WobbleOffset.
	protected override float AimWobbleRadius => 320f;
	protected override float AimWobblePeriod => 280f;

	public override void SetStaticDefaults()
	{
		Main.npcFrameCount[Type] = 1;
		NPCID.Sets.MPAllowedEnemies[Type] = true;
		NPCID.Sets.BossBestiaryPriority.Add(Type);

		NPCID.Sets.SpecificDebuffImmunity[Type] ??= new bool?[BuffLoader.BuffCount];
		NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.OnFire] = true;
		NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.Poisoned] = true;
		NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.Confused] = true;

		Language.GetOrRegister("Mods.GregTechCEuTerraria.NPCs.SoulDistiller.DisplayName", () => "Soul Distiller");
		Language.GetOrRegister("Mods.GregTechCEuTerraria.NPCs.SoulDistiller.Bestiary",
			() => "A distillation tower that drank too deep of the souls it refined and crawled free as a serpent of pipework. Cut it down and it simply distills into its fractions - and each one keeps fighting.");
	}

	public override void SetDefaults()
	{
		// 2x the original 0.62 scale = a ~7-tile-thick tube. Hitbox doubled in
		// step so contact damage matches the visible silhouette (segments share
		// the same scale field via the renderer).
		NPC.scale = 1.24f;
		NPC.width = 80;
		NPC.height = 80;
		NPC.lifeMax = 24000;
		NPC.damage = 50;
		NPC.defense = 18;
		ConfigureCommonDefaults();
	}

	public override void ApplyDifficultyAndPlayerScaling(int numPlayers, float balance, float bossAdjustment)
	{
		NPC.lifeMax = (int)(NPC.lifeMax * balance * bossAdjustment);
	}

	public override void SetBestiary(BestiaryDatabase database, BestiaryEntry bestiaryEntry)
	{
		bestiaryEntry.Info.Add(new FlavorTextBestiaryInfoElement("Mods.GregTechCEuTerraria.NPCs.SoulDistiller.Bestiary"));
	}

	// The parent cycles all four attacks (one reroll to avoid back-to-back repeats).
	protected override int PickAttackState()
	{
		int last = (int)NPC.localAI[0];
		int pick = Main.rand.Next(1, SoulDistillerAttacks.AttackCount + 1);
		if (pick == last) pick = Main.rand.Next(1, SoulDistillerAttacks.AttackCount + 1);
		NPC.localAI[0] = pick;
		return pick;
	}

	// Fractionation at 50% health (server-authoritative).
	protected override bool PreTick(Player target)
	{
		if (_split || Main.netMode == NetmodeID.MultiplayerClient) return false;
		if (NPC.life > NPC.lifeMax / 2) return false;
		Fractionate(target);
		return true;
	}

	private void Fractionate(Player target)
	{
		_split = true;

		SoundEngine.PlaySound(SoundID.NPCDeath14, NPC.Center);
		if (!Main.dedServ)
			for (int i = 0; i < 30; i++)
			{
				var d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.Smoke, 0f, 0f, 80, default, 1.8f);
				d.noGravity = true;
				d.velocity *= 2.4f;
			}

		int fracType = ModContent.NPCType<SoulDistillerFraction>();
		int fracLife = NPC.lifeMax / 4; // 25% of max, each (the "Middle" split)
		for (int f = 0; f < SoulDistillerRenderer.FractionCount; f++)
		{
			int who = NPC.NewNPC(NPC.GetSource_FromAI(), (int)NPC.Center.X, (int)NPC.Center.Y, fracType,
				Start: 0, ai3: f); // ai[3] carries the fraction id (synced)
			if (who >= Main.maxNPCs) continue;

			NPC s = Main.npc[who];
			s.lifeMax = fracLife;
			s.life = fracLife;
			s.target = NPC.target;
			// Fan the four heads outward so they don't stack on spawn.
			float ang = MathHelper.TwoPi * f / SoulDistillerRenderer.FractionCount;
			s.velocity = ang.ToRotationVector2() * 8f;
			s.netUpdate = true;
			NetMessage.SendData(MessageID.SyncNPC, number: who);
		}

		KillChain();
		NPC.active = false;
		if (Main.netMode == NetmodeID.Server)
			NetMessage.SendData(MessageID.SyncNPC, number: NPC.whoAmI);
	}
}
