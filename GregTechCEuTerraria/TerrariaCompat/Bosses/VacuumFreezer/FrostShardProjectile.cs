#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Bosses.Common;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.VacuumFreezer;

// A glittering ice shard vented by the Vacuum Freezer: the upstream diamond
// `gem` texture tinted to a random frost colour with the bright `gem_overlay`
// crystalline sheen on top. Behaviour (arc/straight, dust, light, draw) comes
// from GlowingHostileProjectile; this picks the textures, frost palette, and a
// double Chilled+Frostburn application so eating shards stacks control loss.
public class FrostShardProjectile : GlowingHostileProjectile
{
	public const int PaletteCount = 5;

	// icy white, pale cyan, frost blue, deep ice, glacial teal.
	private static readonly Color[] _palette =
	{
		new(235, 245, 255),
		new(180, 225, 245),
		new(140, 200, 240),
		new(110, 175, 225),
		new(200, 240, 240),
	};

	public override string Texture => "GregTechCEuTerraria/Content/Textures/item/material_sets/diamond/gem";
	protected override string OverlayTexturePath => "GregTechCEuTerraria/Content/Textures/item/material_sets/diamond/gem_overlay";
	protected override Color[] Palette => _palette;

	// Cold trail + light instead of the molten defaults.
	protected override Vector3 GlowColor => new(0.30f, 0.55f, 0.80f);
	protected override int TrailDustType => DustID.IceTorch;
	protected override int HitDebuff => BuffID.Frostburn;
	protected override int HitDebuffDuration => 240;

	// A spray bullet (fans / streams / ingot shrapnel), not a landing projectile -
	// pass through terrain so volleys aren't eaten by the player's arena walls.
	public override void SetDefaults()
	{
		base.SetDefaults();
		Projectile.tileCollide = false;
	}

	// Shards also Chill (movement slow) on top of the inherited Frostburn DoT -
	// the layered control loss that defines the fight.
	public override void OnHitPlayer(Player target, Player.HurtInfo info)
	{
		base.OnHitPlayer(target, info);
		target.AddBuff(BuffID.Chilled, 240);
	}
}
