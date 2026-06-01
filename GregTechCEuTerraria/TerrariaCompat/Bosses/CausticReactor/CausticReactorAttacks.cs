#nullable enable
using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.CausticReactor;

// The Caustic Reactor's six "spell cards" - geometric bullet-hell emitters in
// the Empress-of-Light / Touhou vein: few projectile TYPES (one AcidDroplet),
// rich shapes from pure polar/parametric math. Each is a pure tick-driven
// function over the NPC: called once per AI tick while the spell is active, it
// decides which ticks emit and computes spawn pos + velocity from the geometry,
// then hands them to the boss's `emit` callback (which owns Projectile.NewProjectile
// + palette + damage). SERVER-SIDE ONLY - the boss guards Main.netMode before
// calling; the spawned droplets sync normally.
//
// `t` is ticks since the pattern began emitting (telegraph already elapsed, t>=0).
// Every knob worth tweaking lives in the consts block below, grouped per spell.
internal static class CausticReactorAttacks
{
	internal delegate void Emit(Vector2 pos, Vector2 vel);

	// ---- shared --------------------------------------------------------------
	// Spawn ring radius around the boss core (px) - the shape is drawn at this
	// radius, then inflated outward by each bullet's velocity.
	private const float SpawnR = 42f;

	// Per-card outward bullet speed. Deliberately SPREAD APART so each card has a
	// distinct motion signature (slow graceful bloom vs fast snapping pulse) -
	// the motion difference, paired with the per-card colour, is what makes the
	// cards read as distinct attacks rather than "green dots again". Phase 2
	// scales them all up by Phase2SpeedMul.
	private const float Phase2SpeedMul = 1.25f;
	private const float RoseSpeed   = 2.0f; // graceful slow bloom
	private const float PhylloSpeed = 3.7f; // fast outward spray
	private const float LisSpeed    = 1.7f; // slow lingering weave
	private const float HexSpeed    = 4.6f; // fast snapping rings
	private const float SpiralSpeed = 2.9f; // steady marching arms
	private const float CardSpeed   = 2.4f; // measured heart wave

	private static float Sp(float baseSpeed, bool p2) => baseSpeed * (p2 ? Phase2SpeedMul : 1f);

	// ---- 1. Rose curve  r = R cos(ktheta) ---------------------------------------
	private const float RoseAngStep = 0.42f; // theta advance per emit
	private const int RoseEmitEvery = 2;
	private const int RoseEmitEvery2 = 1;

	// ---- 2. Phyllotaxis (golden-angle sunflower) ----------------------------
	private const float GoldenAngle = 2.39996323f; // 137.5deg in radians
	private const int PhylloEmitEvery = 2;
	private const int PhylloEmitEvery2 = 1;

	// ---- 3. Lissajous lattice  (sin 3t, sin 4t+phi) ---------------------------
	private const float LisW = 0.13f;   // parametric time scale
	private const float LisPhiRate = 0.01f; // phi rotation per tick
	private const float LisA = 1f, LisB = 1f; // axis amplitudes (xSpawnR)
	private const int LisEmitEvery = 2;
	private const int LisEmitEvery2 = 1;

	// ---- 4. Hex lattice pulse ------------------------------------------------
	private const int HexEvery = 14;    // a new expanding ring every N ticks
	private const int HexEvery2 = 9;
	private const float HexRingRot = 0.20f; // each ring rotated from the last

	// ---- 5. Twin counter-rotating Archimedean spirals -----------------------
	// Emits 2 bullets per beat (one per arm) - the densest card, so a slower beat
	// keeps peak concurrency in check without thinning the visible arms.
	private const float SpiralArmRate = 0.16f; // arm angular velocity
	private const int SpiralEmitEvery = 3;
	private const int SpiralEmitEvery2 = 2;

	// ---- 6. Cardioid  r = R(1 - costheta) ---------------------------------------
	private const float CardAngStep = 0.34f;
	private const int CardEmitEvery = 2;
	private const int CardEmitEvery2 = 1;

	private static Vector2 Polar(float len, float ang) => new((float)Math.Cos(ang) * len, (float)Math.Sin(ang) * len);

	// 1. Rose: petals from r = R cos(ktheta). k cycles 3 -> 5 -> 7 across the spell so
	// the petal count visibly changes mid-card. Safe lanes are the gaps between petals.
	public static void Rose(NPC npc, int t, bool phase2, int spellDur, Emit emit)
	{
		if (t % (phase2 ? RoseEmitEvery2 : RoseEmitEvery) != 0) return;
		int third = spellDur / 3;
		int k = t < third ? 3 : t < 2 * third ? 5 : 7;
		float theta = t * RoseAngStep;
		float r = SpawnR * (float)Math.Cos(k * theta);
		// Negative r flips the point across the origin - that's the rose, keep it.
		Vector2 dir = Polar(1f, theta);
		Vector2 pos = npc.Center + dir * r;
		Vector2 vel = (pos - npc.Center).SafeNormalize(dir) * Sp(RoseSpeed, phase2);
		emit(pos, vel);
	}

	// 2. Phyllotaxis: one seed per beat at the golden angle, flung straight out -
	// forms the rotating Vogel-spiral arms of a sunflower head. Dodge = the gap
	// between arms.
	public static void Phyllotaxis(NPC npc, int t, bool phase2, Emit emit)
	{
		if (t % (phase2 ? PhylloEmitEvery2 : PhylloEmitEvery) != 0) return;
		int n = t / (phase2 ? PhylloEmitEvery2 : PhylloEmitEvery);
		float ang = n * GoldenAngle;
		Vector2 dir = Polar(1f, ang);
		emit(npc.Center + dir * SpawnR, dir * Sp(PhylloSpeed, phase2));
	}

	// 3. Lissajous: spawn locus traces (A sin 3wt, B sin 4wt+phi) with phi slowly
	// rotating - a woven, breathing figure. Bullets drift outward from the core.
	public static void Lissajous(NPC npc, int t, bool phase2, Emit emit)
	{
		if (t % (phase2 ? LisEmitEvery2 : LisEmitEvery) != 0) return;
		float phi = t * LisPhiRate;
		float x = LisA * SpawnR * (float)Math.Sin(3f * t * LisW);
		float y = LisB * SpawnR * (float)Math.Sin(4f * t * LisW + phi);
		Vector2 pos = npc.Center + new Vector2(x, y);
		Vector2 vel = (pos - npc.Center).SafeNormalize(Vector2.UnitX) * Sp(LisSpeed, phase2);
		emit(pos, vel);
	}

	// 4. Hex lattice pulse: expanding rings of 6 at hex directions, each ring
	// rotated from the last so the gaps spiral - Empress's expanding-hex homage.
	public static void Hex(NPC npc, int t, bool phase2, Emit emit)
	{
		int every = phase2 ? HexEvery2 : HexEvery;
		if (t % every != 0) return;
		int ring = t / every;
		float rot = ring * HexRingRot;
		float sp = Sp(HexSpeed, phase2);
		for (int i = 0; i < 6; i++)
		{
			float ang = MathHelper.TwoPi * i / 6f + rot;
			Vector2 dir = Polar(1f, ang);
			emit(npc.Center + dir * SpawnR, dir * sp);
		}
	}

	// 5. Twin counter-rotating spirals: two arms sweep opposite directions, each
	// laying a stream of bullets - two rotating safe lanes to weave through.
	public static void Spiral(NPC npc, int t, bool phase2, Emit emit)
	{
		if (t % (phase2 ? SpiralEmitEvery2 : SpiralEmitEvery) != 0) return;
		float theta = t * SpiralArmRate;
		float sp = Sp(SpiralSpeed, phase2);
		// Arm A sweeps +theta, arm B sweeps -theta (counter-rotating), 180deg apart.
		float a = theta;
		float b = -theta + MathHelper.Pi;
		Vector2 dA = Polar(1f, a), dB = Polar(1f, b);
		emit(npc.Center + dA * SpawnR, dA * sp);
		emit(npc.Center + dB * SpawnR, dB * sp);
	}

	// 6. Cardioid finale: a heart-shaped acid wave r = R(1-costheta). Ground pools are
	// spawned separately by the boss (the only spell with terrain hazards).
	public static void Cardioid(NPC npc, int t, bool phase2, Emit emit)
	{
		if (t % (phase2 ? CardEmitEvery2 : CardEmitEvery) != 0) return;
		float theta = t * CardAngStep;
		float r = SpawnR * (1f - (float)Math.Cos(theta));
		Vector2 dir = Polar(1f, theta);
		Vector2 pos = npc.Center + dir * r;
		Vector2 vel = (pos - npc.Center).SafeNormalize(dir) * Sp(CardSpeed, phase2);
		emit(pos, vel);
	}
}
