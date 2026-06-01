#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Bosses.Common;
using Microsoft.Xna.Framework;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.SoulDistiller;

// A squirted glob of hydrocarbon - the Soul Distiller's bread-and-butter spray
// (and the rain the oil clouds drip). An arcing droplet tinted to one of the
// four fractions, igniting on hit (everything it sprays is flammable). Behaviour
// comes from GlowingHostileProjectile; this picks the droplet texture, the
// fraction palette, and a subtler glow/dust than the molten ingot.
//
// Spawner sets ai[0]=1 to arc under gravity (else straight), ai[1]=fraction index.
public class LiquidGlobProjectile : GlowingHostileProjectile
{
	protected override Color[] Palette => SoulDistillerRenderer.Fractions;
	protected override int HitDebuff => BuffID.OnFire;
	protected override Vector3 GlowColor => new(0.12f, 0.10f, 0.06f);
	protected override int TrailDustType => DustID.Smoke;
	protected override float SpinSpeed => 0.0f; // a droplet shouldn't pinwheel

	public override string Texture => "GregTechCEuTerraria/Content/Textures/gui/icon/bucket_mode/water_drop";

	public override void SetDefaults()
	{
		base.SetDefaults();
		Projectile.width = 16;
		Projectile.height = 16;
		Projectile.scale = 1.5f;
		Projectile.timeLeft = 240;
	}
}
