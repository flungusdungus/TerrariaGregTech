#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Bosses.Common;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.SoulDistiller;

// One of the four sub-worms the Soul Distiller distills into at 50% health. Its
// fraction id rides in ai[3] (synced); that drives its colour AND its single
// signature attack - heavy oil rains burning clouds, light oil sprays globs,
// naphtha lobs buckets, refinery gas belches toxic tail-vent. Shorter than the
// parent. Spawned only by SoulDistiller.Fractionate (no natural spawn).
[AutoloadBossHead]
public class SoulDistillerFraction : SoulDistillerHeadBase
{
	public override string Texture => "GregTechCEuTerraria/Content/Textures/block/casings/solid/machine_casing_clean_stainless_steel";
	public override string BossHeadTexture => "GregTechCEuTerraria/Content/Textures/block/casings/solid/machine_casing_clean_stainless_steel";

	protected override int SegmentCount => 28;
	public override int Fraction => (int)NPC.ai[3];

	protected override int AttackInterval => 64;
	protected override Color HeadTint => SoulDistillerRenderer.Fractions[Clamp(Fraction)];
	protected override Color HeadGlowColor => SoulDistillerRenderer.Fractions[Clamp(Fraction)];

	// A touch faster + tighter-turning than the parent (smaller worms feel more
	// agile) but still a wide, dodgeable arc - NOT a tracking laser. See
	// SoulDistiller.MoveConfig for the speed/turn rationale. GapDistance scales
	// with the doubled NPC.scale below so segments space proportionally.
	protected override WormMovementConfig MoveConfig => new()
	{
		MaxSpeed = 13.5f,
		Acceleration = 0.24f,
		TurnRate = 0.038f,
		MinSpeedFrac = 0.6f,
		GapDistance = 50f,
	};

	// Smaller wobble than the parent (smaller worms feel more agile) but enough
	// to break a perfect track. ~14 tiles + slightly faster sweep.
	protected override float AimWobbleRadius => 220f;
	protected override float AimWobblePeriod => 220f;

	public override void SetStaticDefaults()
	{
		Main.npcFrameCount[Type] = 1;
		NPCID.Sets.MPAllowedEnemies[Type] = true;

		NPCID.Sets.SpecificDebuffImmunity[Type] ??= new bool?[BuffLoader.BuffCount];
		NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.OnFire] = true;
		NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.Poisoned] = true;
		NPCID.Sets.SpecificDebuffImmunity[Type][BuffID.Confused] = true;

		Language.GetOrRegister("Mods.GregTechCEuTerraria.NPCs.SoulDistillerFraction.DisplayName", () => "Soul Distiller");
	}

	public override void SetDefaults()
	{
		// 2x the original 0.5 scale - still thinner than the parent (1.24) but
		// reads as substantial (~5.5-tile tube). Hitbox doubled in step.
		NPC.scale = 1.0f;
		NPC.width = 64;
		NPC.height = 64;
		NPC.lifeMax = 6000; // overwritten by SoulDistiller.Fractionate at spawn
		NPC.damage = 44;
		NPC.defense = 16;
		ConfigureCommonDefaults();
	}

	// Each fraction specialises in one attack (state = SoulDistillerAttacks index + 1).
	protected override int PickAttackState() => Clamp(Fraction) switch
	{
		SoulDistillerRenderer.LightOil => SoulDistillerAttacks.Spray + 1,
		SoulDistillerRenderer.Naphtha  => SoulDistillerAttacks.Buckets + 1,
		SoulDistillerRenderer.HeavyOil => SoulDistillerAttacks.OilCloud + 1,
		_ /* RefineryGas */            => SoulDistillerAttacks.GasBelch + 1,
	};

	private static int Clamp(int f) =>
		(f % SoulDistillerRenderer.FractionCount + SoulDistillerRenderer.FractionCount) % SoulDistillerRenderer.FractionCount;
}
