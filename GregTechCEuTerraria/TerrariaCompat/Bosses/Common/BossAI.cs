#nullable enable
using Microsoft.Xna.Framework;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.Common;

// Boss-agnostic flight + targeting helpers shared by flying bosses. Pure
// functions over an NPC - no per-boss state, safe to call from any ModNPC.AI().
public static class BossAI
{
	// Steer velocity toward `dest`, easing the target speed down within
	// `easeRadius` px so the boss settles instead of overshooting and
	// flip-flopping its velocity (which reads as buggy jitter / a snapping tilt).
	public static void MoveToward(NPC npc, Vector2 dest, float maxSpeed, float accel, float easeRadius = 120f)
	{
		Vector2 toDest = dest - npc.Center;
		float dist = toDest.Length();
		float speed = dist < easeRadius ? maxSpeed * (dist / easeRadius) : maxSpeed;
		Vector2 desired = dist > 4f ? toDest / dist * speed : Vector2.Zero;
		npc.velocity = Vector2.Lerp(npc.velocity, desired, accel);
	}

	// Ease the body tilt toward a velocity-driven target, hard-capping the
	// per-frame change so a sudden velocity jump (a dash, a fast reposition)
	// can never snap the rotation.
	public static void SmoothTilt(NPC npc, float perVelocity = 0.018f, float maxTilt = 0.18f,
	                              float ease = 0.1f, float maxStep = 0.015f)
	{
		float target = MathHelper.Clamp(npc.velocity.X * perVelocity, -maxTilt, maxTilt);
		float eased = MathHelper.Lerp(npc.rotation, target, ease);
		npc.rotation += MathHelper.Clamp(eased - npc.rotation, -maxStep, maxStep);
	}

	// Two-pass target acquisition (re-targets if the current one is gone).
	// Returns false when no valid player remains - the caller should despawn.
	public static bool TryAcquireTarget(NPC npc, out Player target)
	{
		if (npc.target < 0 || npc.target == Main.maxPlayers ||
		    Main.player[npc.target].dead || !Main.player[npc.target].active)
			npc.TargetClosest();

		target = Main.player[npc.target];
		return target.active && !target.dead;
	}

	// Fly up and out, then despawn. Call when TryAcquireTarget returns false.
	// (Per-boss ai[] state resets stay in the caller - this only does motion.)
	public static void FlyAwayDespawn(NPC npc)
	{
		npc.velocity.X *= 0.95f;
		npc.velocity.Y -= 0.4f;
		if (npc.velocity.Y < -16f) npc.velocity.Y = -16f;
		npc.EncourageDespawn(10);
	}
}
