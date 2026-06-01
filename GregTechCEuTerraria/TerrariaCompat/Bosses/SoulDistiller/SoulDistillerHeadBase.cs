#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Bosses.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.SoulDistiller;

// Shared head of the Soul Distiller worm family - the parent (all four attacks)
// and the four fraction sub-worms (one attack each) only differ in stats, attack
// selection, colour, and the parent's fractionation. Everything else lives here:
// the move-and-attack state loop, the composite draw, and the "last worm dead ->
// loot + downed flag" finish.
//
// ai[0] = state (0 = moving; 1..4 = a firing attack, index = state-1)
// ai[1] = state timer
// ai[2], ai[3] = free for subclass use (the fraction stores its id in a field, not ai)
public abstract class SoulDistillerHeadBase : WormBossHead, IDebuggableBoss
{
	private const int S_Move = 0;
	private const int ActiveLen = 10; // ticks a firing state lasts before returning to moving

	private static readonly HashSet<int> _swappedHeadTypes = new();

	protected override int BodyType => ModContent.NPCType<SoulDistillerBody>();
	protected override int TailType => ModContent.NPCType<SoulDistillerTail>();

	// -1 = parent (random fraction colour per glob), 0..3 = a fraction sub-worm.
	public virtual int Fraction => -1;

	protected virtual int AttackInterval => 70;
	protected virtual int GlobDamage => 30;
	protected virtual int BucketDamage => 42;
	protected virtual int GasDamage => 24;

	// Head body colour + emissive glow colour (overridden by the fraction worms).
	protected virtual Color HeadTint => Color.Lerp(Color.White, SoulDistillerRenderer.GradientColor(0f), 0.5f);
	protected virtual Color HeadGlowColor => SoulDistillerRenderer.GradientColor(0f);

	// Aim noise: the head seeks (player.Center + WobbleOffset(this, radius, period))
	// instead of the player's exact centre, so it can't laser-track. Larger radius
	// + longer period = looser tracking + the worm misses past the player more
	// often. Tuned per-subclass; 0 disables.
	protected virtual float AimWobbleRadius => 0f;
	protected virtual float AimWobblePeriod => 240f;

	// Server-side: which firing state (1..4) to enter next.
	protected abstract int PickAttackState();

	// Hook for the parent's fractionation check. Return true if the head removed
	// itself this tick (caller stops processing).
	protected virtual bool PreTick(Player target) => false;

	// Shared NPC.SetDefaults body - the subclass sets size/life/defense then calls this.
	protected void ConfigureCommonDefaults()
	{
		NPC.aiStyle = -1;
		NPC.noGravity = true;
		NPC.noTileCollide = true;
		NPC.knockBackResist = 0f;
		NPC.behindTiles = false;
		NPC.boss = true;
		NPC.HitSound = SoundID.NPCHit4;
		NPC.DeathSound = SoundID.NPCDeath14;
		NPC.npcSlots = 12f;
		NPC.value = Item.buyPrice(gold: 5);
		NPC.BossBar = ModContent.GetInstance<SoulDistillerBossBar>();
		NPC.SpawnWithHigherTime(30);
		if (!Main.dedServ)
			Music = MusicID.Boss2;
	}

	protected override void HeadAI(Player target)
	{
		Lighting.AddLight(NPC.Center, HeadGlowColor.R / 400f, HeadGlowColor.G / 400f, HeadGlowColor.B / 400f);

		if (PreTick(target) || !NPC.active) return;

		// The worm never stops - move every tick regardless of attack state. Aim
		// at a wobbled ghost point near the player, not the player's exact centre,
		// so the head can't laser-track (vanilla Destroyer / Calamity Devourer
		// shape). Subclasses tune the wobble radius + period.
		Vector2 wobbled = target.Center + WormAI.WobbleOffset(NPC, AimWobbleRadius, AimWobblePeriod);
		Seek(wobbled);

		bool server = Main.netMode != NetmodeID.MultiplayerClient;
		int state = (int)NPC.ai[0];

		if (state == S_Move)
		{
			NPC.ai[1]++;
			if (NPC.ai[1] >= AttackInterval && server)
			{
				NPC.ai[1] = 0f;
				NPC.ai[0] = PickAttackState();
				NPC.netUpdate = true;
			}
			return;
		}

		// Firing state: fire the volley on entry (all clients -> sound; server ->
		// spawns), hold briefly, then return to moving.
		if (NPC.ai[1] == 0f)
			SoulDistillerAttacks.Perform(state - 1, NPC, target, Fraction, GlobDamage, BucketDamage, GasDamage);

		NPC.ai[1]++;
		if (NPC.ai[1] >= ActiveLen)
		{
			NPC.ai[0] = S_Move;
			NPC.ai[1] = 0f;
			if (server) NPC.netUpdate = true;
		}
	}

	public override void OnKill()
	{
		base.OnKill(); // kill our trailing chain

		// The four fraction worms are all boss=true (for music / no-despawn / bar
		// tracking), so vanilla would broadcast "Soul Distiller has been defeated!"
		// and drop boss hearts/potions once per head. OnKill runs BEFORE
		// DoDeathEvents (the announce + the heart/potion drop), both gated on
		// NPC.boss - so clear boss on every head except the last to fire them once.
		if (AnyOtherHeadAlive())
		{
			NPC.boss = false;
			return;
		}

		// Last head down -> the boss is defeated.
		if (Main.netMode != NetmodeID.MultiplayerClient)
		{
			SoulDistillerWorld.MarkDowned();
			DropFinalLoot();
		}
	}

	private bool AnyOtherHeadAlive()
	{
		for (int i = 0; i < Main.maxNPCs; i++)
		{
			NPC n = Main.npc[i];
			if (n.active && i != NPC.whoAmI && n.ModNPC is SoulDistillerHeadBase)
				return true;
		}
		return false;
	}

	// Server-side final loot: the HV-age GregTech boss drops (config-gated), spawned
	// directly since this is a multi-NPC boss (no single ModifyNPCLoot owner).
	private void DropFinalLoot()
	{
		if (!ModContent.GetInstance<global::GregTechCEuTerraria.Config.GTConfig>().EnableBossDrops) return;
		var src = NPC.GetSource_Death();
		foreach (var d in BossDrops.BossDropRegistry.GetTierDrops(3, withComponents: true))
		{
			int stack = Main.rand.Next(d.Min, d.Max + 1);
			if (stack > 0)
				Item.NewItem(src, NPC.Center, Vector2.Zero, d.ItemType, stack);
		}
	}

	public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
	{
		if (_swappedHeadTypes.Add(Type))
		{
			bool s = false;
			BossHeadHelper.SwapBakedHead(NPC, SoulDistillerRenderer.BossHeadAsset, ref s);
		}

		Texture2D? body = SoulDistillerRenderer.HeadBody;
		if (body is null) return true;

		Vector2 pos = NPC.Center - screenPos;
		var origin = body.Size() * 0.5f;
		spriteBatch.Draw(body, pos, null, SoulDistillerRenderer.Tint(drawColor, HeadTint),
			NPC.rotation, origin, NPC.scale, SpriteEffects.None, 0f);

		Texture2D? glow = SoulDistillerRenderer.HeadGlow;
		if (glow is not null)
		{
			float pulse = 0.6f + 0.2f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 4f);
			spriteBatch.Draw(glow, pos, null, HeadGlowColor * pulse,
				NPC.rotation, glow.Size() * 0.5f, NPC.scale, SpriteEffects.None, 0f);
		}
		return false;
	}

	public override void HitEffect(NPC.HitInfo hit)
	{
		int n = NPC.life <= 0 ? 20 : 3;
		for (int i = 0; i < n; i++)
		{
			var d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.Smoke, hit.HitDirection, -1f, 90, default, 1.1f);
			d.noGravity = true;
		}
	}

	// ---- IDebuggableBoss (GTConfig.DebugMobs overlay) ---------------------
	// Shared by the parent worm AND every fraction sub-worm. Each subclass
	// inherits HeadLabel and overrides if it wants a different title.

	protected virtual string HeadLabel => Fraction < 0
		? "Soul Distiller"
		: $"Distiller Fraction [{Fraction switch { 0 => "RefineryGas", 1 => "Naphtha", 2 => "LightOil", _ => "HeavyOil" }}]";

	public string CurrentAttackLabel() => AttackName((int)NPC.ai[0]);

	private static string AttackName(int state) =>
		state == 0 ? "Move" : (state - 1) switch
		{
			SoulDistillerAttacks.Spray    => "Spray",
			SoulDistillerAttacks.Buckets  => "Buckets",
			SoulDistillerAttacks.OilCloud => "OilCloud",
			SoulDistillerAttacks.GasBelch => "GasBelch",
			_ => $"?({state})",
		};

	public void BuildDebugLines(List<string> lines)
	{
		int state = (int)NPC.ai[0];
		int t = (int)NPC.ai[1];
		float hpPct = NPC.lifeMax > 0 ? 100f * NPC.life / NPC.lifeMax : 0f;
		Player p = Main.player[NPC.target];
		float dist = p?.active == true ? Vector2.Distance(NPC.Center, p.Center) : 0f;
		WormMovementConfig cfg = MoveConfig;
		lines.Add($"{HeadLabel}  HP {NPC.life}/{NPC.lifeMax} ({hpPct:0.0}%)");
		lines.Add($"State: {AttackName(state)}   t={t}/{AttackInterval}");
		lines.Add($"Turn: {cfg.TurnRate:F3}rad/t  Speed: {NPC.velocity.Length():0.0}/{cfg.MaxSpeed:0.0}");
		lines.Add($"Wobble: r={AimWobbleRadius:0}  T={AimWobblePeriod:0}t   Target dist: {dist:0}px");
	}

	public void DrawDebugGizmos(SpriteBatch sb, Vector2 screenPos)
	{
		Player p = Main.player[NPC.target];
		if (p is null || !p.active) return;

		// Aim crosshair on the wobbled target point - the worm's actual
		// steering target each tick. No head -> aim line (long screen-spanning
		// rectangle); the velocity vector is the useful directional gizmo.
		Vector2 aim = p.Center + WormAI.WobbleOffset(NPC, AimWobbleRadius, AimWobblePeriod);
		DebugOverlaySystem.DrawCrosshair(sb, aim, screenPos,
			new Color(255, 200, 100), 10f, 1);

		// Heading pip - a small cyan square 60 px ahead of the head in the
		// current velocity direction. Replaces the long velocity-projection
		// line, which used DrawLine's rotated-MagicPixel path and produced
		// phantom screen-spanning rectangles. Axis-aligned, fixed-size, no
		// rotation. Speed is in the text panel; direction is the pip.
		if (NPC.velocity.LengthSquared() > 0.5f)
		{
			Vector2 pip = NPC.Center + NPC.velocity.SafeNormalize(Vector2.Zero) * 60f;
			int px = (int)(pip.X - screenPos.X);
			int py = (int)(pip.Y - screenPos.Y);
			DebugOverlaySystem.DrawRectBorder(sb,
				new Rectangle(px - 4, py - 4, 8, 8),
				new Color(120, 240, 255), 1);
		}
	}
}
