#nullable enable
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.Common;

// Reusable body/tail segment of a worm boss. Follows the segment ahead, shares
// the head's realLife (so damage pools onto the head), and self-destructs if the
// chain ahead is severed. Subclass supplies defaults (size/life/defense/texture)
// + draw; flag the last segment with IsTail for a tapered cap.
//
// ai map (set by WormAI.SpawnChain): ai[0]=head index, ai[1]=ahead index,
// ai[2]=0..1 ratio along the body (for the head->tail colour gradient),
// ai[3]=ordinal index along the chain (0-based).
public abstract class WormBossSegment : ModNPC
{
	protected virtual bool IsTail => false;
	protected virtual float GapDistance => 38f;

	protected int HeadIndex => (int)NPC.ai[0];
	protected float GradientRatio => NPC.ai[2];
	protected int SegmentIndex => (int)NPC.ai[3];

	// The head NPC this segment belongs to, or null if the chain is broken.
	protected NPC? Head
	{
		get
		{
			int h = HeadIndex;
			if (h < 0 || h >= Main.npc.Length) return null;
			NPC n = Main.npc[h];
			return n.active ? n : null;
		}
	}

	public override void AI()
	{
		NPC.noGravity = true;
		NPC.noTileCollide = true;

		if (!WormAI.FollowAhead(NPC, GapDistance))
		{
			NPC.life = 0;
			NPC.HitEffect();
			NPC.active = false;
			return;
		}
		if (NPC.timeLeft < 1800) NPC.timeLeft = 1800;
	}
}
