#nullable enable
using GregTechCEuTerraria.Config;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.Common;

// Feeds BossFightTracker with damage dealt BY the local player TO any boss
// implementing IDebuggableBoss (parent + every chained body/tail for a worm boss,
// via shared realLife). Used for the player-DPS readout. Skipped when DebugMobs
// is off.
public class BossFightDamageDealtGlobalNPC : GlobalNPC
{
	public override bool InstancePerEntity => false;

	public override void OnHitByProjectile(NPC npc, Projectile proj, NPC.HitInfo hit, int dmgDone)
	{
		if (!GTConfig.Instance.DebugMobs) return;
		if (!IsBossOrSegment(npc)) return;
		BossFightTracker.RecordBossHit(dmgDone);
	}

	public override void OnHitByItem(NPC npc, Player player, Item item, NPC.HitInfo hit, int dmgDone)
	{
		if (!GTConfig.Instance.DebugMobs) return;
		if (player.whoAmI != Main.myPlayer) return; // local damage only
		if (!IsBossOrSegment(npc)) return;
		BossFightTracker.RecordBossHit(dmgDone);
	}

	// True for any debuggable-boss NPC OR a worm segment whose head is one
	// (segments share `realLife` with the head; their own ModNPC isn't the
	// IDebuggableBoss but they ARE damage-pool members of the fight).
	private static bool IsBossOrSegment(NPC npc)
	{
		if (npc.ModNPC is IDebuggableBoss) return true;
		if (npc.realLife >= 0 && npc.realLife < Main.maxNPCs
		    && Main.npc[npc.realLife].ModNPC is IDebuggableBoss)
			return true;
		return false;
	}
}
