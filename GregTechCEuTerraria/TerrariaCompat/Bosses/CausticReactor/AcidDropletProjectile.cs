#nullable enable
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.CausticReactor;

// One bullet type for every Caustic Reactor pattern; emitter math
// (CausticReactorAttacks) owns the motion. Palette-tinted via ai[1].
public class AcidDropletProjectile : Common.GlowingHostileProjectile
{
	public const int PaletteCount = 14;

	// Index matches CausticReactor.PaletteFor: 0-6 = phase-1 corrosive
	// (chaos/rose/phyllo/lissaj/hex/spiral/cardio), 7-13 = phase-2 violet mirror.
	private static readonly Color[] _palette =
	{
		new(170, 210, 120), new(140, 220, 60), new(225, 225, 80), new(70, 205, 160),
		new(185, 240, 55), new(205, 160, 50), new(120, 215, 110),
		new(200, 150, 235), new(185, 105, 230), new(215, 140, 245), new(150, 95, 230),
		new(205, 120, 240), new(170, 90, 220), new(205, 130, 250),
	};

	public override string Texture => "GregTechCEuTerraria/Content/Textures/item/material_sets/emerald/gem";
	protected override string OverlayTexturePath => "GregTechCEuTerraria/Content/Textures/item/material_sets/emerald/gem_overlay";
	protected override Color[] Palette => _palette;

	protected override Vector3 GlowColor => new(0.42f, 0.62f, 0.20f);
	protected override int TrailDustType => DustID.GreenFairy;
	protected override int HitDebuff => BuffID.Venom;
	protected override int HitDebuffDuration => 240;
	protected override float SpinSpeed => 0.12f;

	// Cheap per bullet (200+ live in a dense spell): sparse trail + staggered light.
	protected override int TrailDustChance => 10;
	protected override int LightInterval => 3;

	public override void SetDefaults()
	{
		base.SetDefaults();
		Projectile.tileCollide = false; // patterns stay intact over terrain
		Projectile.width = 26;
		Projectile.height = 26;
		Projectile.scale = 2f;
		Projectile.timeLeft = 200; // caps peak concurrency
	}
}
