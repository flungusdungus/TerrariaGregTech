#nullable enable
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.Common;

// Head-movement knobs for a segmented worm boss.
public struct WormMovementConfig
{
	public float MaxSpeed;      // top px/tick the head seeks at
	public float Acceleration;  // px/tick the head ramps speed by
	public float TurnRate;      // max radians/tick the heading may rotate (lower = wider arcs)
	public float MinSpeedFrac;  // speed floor as a fraction of MaxSpeed (never fully stalls)
	public float GapDistance;   // spacing between segment centres, px

	public static WormMovementConfig Default => new()
	{
		MaxSpeed = 9f,
		Acceleration = 0.22f,
		TurnRate = 0.10f,
		MinSpeedFrac = 0.5f,
		GapDistance = 38f,
	};
}

// Boss-agnostic segmented-worm helpers - chain spawning, leader-follow, head
// steering. Mirrors vanilla aiStyle 6 (The Destroyer), split so a per-boss head
// can run its own attack machine on top. Sibling to the flying-boss BossAI.
public static class WormAI
{
	// SERVER-ONLY. Spawn `count` trailing segments (count-1 body + 1 tail), wired
	// follow-the-leader. Mirrors The Destroyer's spawn loop (NPC.cs ~50205); every
	// segment shares head.realLife so damage pools onto the head.
	//
	// Segment ai: [0]=head whoAmI, [1]=segment-ahead index,
	//   [2]=0..1 body ratio (colour gradient), [3]=chain ordinal (per-segment art).
	public static void SpawnChain(NPC head, int bodyType, int tailType, int count, IEntitySource src)
	{
		if (Main.netMode == NetmodeID.MultiplayerClient) return;

		head.realLife = head.whoAmI;
		int ahead = head.whoAmI;
		for (int i = 0; i < count; i++)
		{
			int type = i == count - 1 ? tailType : bodyType;
			int who = NPC.NewNPC(src, (int)head.Center.X, (int)head.Center.Y, type, head.whoAmI);
			if (who >= Main.maxNPCs) break;

			NPC seg = Main.npc[who];
			seg.realLife = head.whoAmI;
			seg.ai[0] = head.whoAmI;
			seg.ai[1] = ahead;
			seg.ai[2] = (float)(i + 1) / (count + 1);
			seg.ai[3] = i;
			seg.netUpdate = true;
			NetMessage.SendData(MessageID.SyncNPC, number: who);
			ahead = who;
		}
	}

	// Snap to keep `gap` px behind the segment ahead, facing it. Runs on all
	// clients. Returns false when the segment ahead is gone (caller self-destructs).
	public static bool FollowAhead(NPC seg, float gap)
	{
		int aheadIdx = (int)seg.ai[1];
		if (aheadIdx < 0 || aheadIdx >= Main.npc.Length) return false;

		NPC ahead = Main.npc[aheadIdx];
		if (!ahead.active) return false;

		Vector2 toAhead = ahead.Center - seg.Center;
		float dist = toAhead.Length();
		if (dist > 0.01f)
		{
			seg.rotation = toAhead.ToRotation() + MathHelper.PiOver2;
			if (dist > gap)
				seg.Center += toAhead / dist * (dist - gap);
		}
		seg.velocity = Vector2.Zero;
		return true;
	}

	// Deterministic per-NPC aim wobble - a lagging elliptical drift around the
	// target so the head tracks a ghost point, not the player exactly. Derives
	// from synced state (GameUpdateCount + whoAmI) so every client agrees without
	// a packet. `period` = ticks per sweep (slower = looser tracking).
	public static Vector2 WobbleOffset(NPC npc, float radius, float period = 240f)
	{
		if (radius <= 0f) return Vector2.Zero;
		float t = Main.GameUpdateCount / MathHelper.Max(1f, period);
		float seed = npc.whoAmI * 0.413f;
		float x = (float)System.Math.Sin((t + seed) * MathHelper.TwoPi);
		// Different period on Y -> irrational Lissajous, not a clean circle.
		float y = (float)System.Math.Sin((t * 1.37f + seed * 1.71f) * MathHelper.TwoPi);
		return new Vector2(x, y) * radius;
	}

	// Steer the head toward `target`: ease speed toward MaxSpeed (floored at
	// MinSpeedFrac), rotate heading by at most TurnRate/tick so it arcs not snaps.
	// Sprite faces heading + PiOver2 (vanilla worm convention).
	public static void DriveHead(NPC head, Vector2 target, in WormMovementConfig cfg)
	{
		float speed = MathHelper.Clamp(head.velocity.Length() + cfg.Acceleration, 0f, cfg.MaxSpeed);
		float floor = cfg.MaxSpeed * cfg.MinSpeedFrac;
		if (speed < floor) speed = floor;

		float desired = (target - head.Center).ToRotation();
		float current = head.velocity.LengthSquared() < 0.01f ? desired : head.velocity.ToRotation();
		float delta = MathHelper.Clamp(MathHelper.WrapAngle(desired - current), -cfg.TurnRate, cfg.TurnRate);
		float heading = current + delta;

		head.velocity = heading.ToRotationVector2() * speed;
		head.rotation = heading + MathHelper.PiOver2;
	}
}
