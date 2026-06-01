#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Bosses.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.SoulDistiller;

// Body/tail segments of a Soul Distiller worm. Both draw a baked stainless
// "tower-ring" tinted by their head's fraction (uniform for a sub-worm, or a
// head->tail gradient on the parent). Damage pools onto the head via realLife.
public abstract class SoulDistillerSegment : WormBossSegment
{
	public override string Texture => "GregTechCEuTerraria/Content/Textures/block/casings/solid/machine_casing_clean_stainless_steel";

	protected abstract Texture2D? SegTexture { get; }
	protected override float GapDistance => 30f * NPC.scale;

	protected void ConfigureSegmentDefaults(int life, int defense, int damage)
	{
		NPC.aiStyle = -1;
		NPC.noGravity = true;
		NPC.noTileCollide = true;
		NPC.knockBackResist = 0f;
		NPC.lifeMax = life;
		NPC.defense = defense;
		NPC.damage = damage;
		NPC.HitSound = SoundID.NPCHit4;
		NPC.behindTiles = false;
		NPC.dontCountMe = true; // don't let the long chain skew spawn/biome counts
	}

	public override void AI()
	{
		// Match the owning head's scale so a fraction sub-worm renders thinner.
		NPC? h = Head;
		if (h is not null) NPC.scale = h.scale;
		base.AI();
	}

	private Color SegTint(Color light)
	{
		NPC? h = Head;
		Color frac = h?.ModNPC is SoulDistillerHeadBase hb && hb.Fraction >= 0
			? SoulDistillerRenderer.Fractions[hb.Fraction]
			: SoulDistillerRenderer.GradientColor(GradientRatio);
		return SoulDistillerRenderer.Tint(light, frac);
	}

	public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
	{
		// Baked strip, or the autoload casing as a fallback so a failed/late bake
		// never leaves the segment invisible.
		Texture2D? tex = SegTexture ?? TextureAssets.Npc[Type]?.Value;
		if (tex is null) return true;
		Vector2 pos = NPC.Center - screenPos;
		spriteBatch.Draw(tex, pos, null, SegTint(drawColor), NPC.rotation, tex.Size() * 0.5f, NPC.scale, SpriteEffects.None, 0f);
		return false;
	}

	public override void HitEffect(NPC.HitInfo hit)
	{
		if (NPC.life <= 0)
			for (int i = 0; i < 8; i++)
			{
				var d = Dust.NewDustDirect(NPC.position, NPC.width, NPC.height, DustID.Smoke, hit.HitDirection, -1f, 90, default, 1.1f);
				d.noGravity = true;
			}
	}
}

// A mid-body distillation ring. Plain stainless, except every 4th segment which
// carries the pipe/hatch overlay - so the tower reads as banded plates + hatches.
public class SoulDistillerBody : SoulDistillerSegment
{
	protected override Texture2D? SegTexture =>
		(SegmentIndex + 1) % 4 == 0 ? SoulDistillerRenderer.BodyHatch : SoulDistillerRenderer.BodyPlain;

	public override void SetStaticDefaults()
	{
		Main.npcFrameCount[Type] = 1;
		NPCID.Sets.MPAllowedEnemies[Type] = true;
	}

	public override void SetDefaults()
	{
		NPC.width = 30;
		NPC.height = 30;
		ConfigureSegmentDefaults(life: 5000, defense: 20, damage: 44);
	}
}

// The reboiler end-cap (vented grate).
public class SoulDistillerTail : SoulDistillerSegment
{
	protected override Texture2D? SegTexture => SoulDistillerRenderer.Tail;
	protected override bool IsTail => true;

	public override void SetStaticDefaults()
	{
		Main.npcFrameCount[Type] = 1;
		NPCID.Sets.MPAllowedEnemies[Type] = true;
	}

	public override void SetDefaults()
	{
		NPC.width = 28;
		NPC.height = 28;
		ConfigureSegmentDefaults(life: 5000, defense: 14, damage: 40);
	}
}
