#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Bosses.Common;
using Microsoft.Xna.Framework;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.FallenEBF;

// A glowing freshly-smelted ingot hurled by the Fallen EBF: the upstream
// `ingot_hot` texture tinted to a random hot metal with the bright
// `ingot_hot_overlay` molten layer on top. All behaviour (arc/straight, dust,
// light, On Fire!, draw) comes from GlowingHostileProjectile - this only picks
// the textures + metal palette.
public class HotIngotProjectile : GlowingHostileProjectile
{
	public const int PaletteCount = 5;

	public override void SetDefaults()
	{
		base.SetDefaults();
		// The base GlowingHostileProjectile defaults to a 40x40 hitbox at scale
		// 3 (~48px visible), which felt unfair - you'd take damage from clear
		// air. Shrink the hitbox to 22x22 (~half the visible sprite) so contact
		// damage lands only when the ingot visibly touches you. Sprite scale
		// unchanged.
		Projectile.width = 22;
		Projectile.height = 22;
	}

	// copper-hot, iron white-hot, steel grey, bronze, gold.
	private static readonly Color[] _palette =
	{
		new(255, 140, 50),
		new(255, 210, 160),
		new(210, 215, 225),
		new(230, 165, 95),
		new(255, 205, 80),
	};

	public override string Texture => "GregTechCEuTerraria/Content/Textures/item/material_sets/metallic/ingot_hot";
	protected override string OverlayTexturePath => "GregTechCEuTerraria/Content/Textures/item/material_sets/metallic/ingot_hot_overlay";
	protected override Color[] Palette => _palette;
}
