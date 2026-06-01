#nullable enable
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.Common;

// Reusable head of a segmented worm boss. Owns the lifecycle the subclass
// shouldn't have to re-write: spawn the trailing chain once, acquire/lose the
// target, keep timeLeft topped up, kill the chain on death. The subclass fills
// in the segment types + count, the movement feel, and its attack state machine
// (HeadAI), and is free to use the full ai[0..3] for attack state - chaining
// uses the built-in realLife field, not ai[].
public abstract class WormBossHead : ModNPC
{
	protected abstract int BodyType { get; }
	protected abstract int TailType { get; }
	protected abstract int SegmentCount { get; }
	protected virtual WormMovementConfig MoveConfig => WormMovementConfig.Default;

	private bool _spawnedChain;

	// Per-tick boss logic (movement + attacks), called with a living target.
	// Drive movement via Seek(); the subclass owns ai[0..3].
	protected abstract void HeadAI(Player target);

	// Steer toward a point using this head's movement config.
	protected void Seek(Microsoft.Xna.Framework.Vector2 target)
	{
		WormMovementConfig cfg = MoveConfig;
		WormAI.DriveHead(NPC, target, in cfg);
	}

	public override void AI()
	{
		if (!_spawnedChain && Main.netMode != NetmodeID.MultiplayerClient)
		{
			_spawnedChain = true;
			WormAI.SpawnChain(NPC, BodyType, TailType, SegmentCount, NPC.GetSource_FromAI());
		}

		NPC.noGravity = true;
		NPC.noTileCollide = true;

		if (!BossAI.TryAcquireTarget(NPC, out Player target))
		{
			NPC.velocity.Y -= 0.3f;
			NPC.EncourageDespawn(10);
			return;
		}
		if (NPC.timeLeft < 1800) NPC.timeLeft = 1800;

		HeadAI(target);
	}

	// When the head dies, take its whole chain with it (everything sharing this
	// head's realLife). Server broadcasts each removal.
	public override void OnKill()
	{
		KillChain();
	}

	protected void KillChain()
	{
		for (int i = 0; i < Main.maxNPCs; i++)
		{
			NPC n = Main.npc[i];
			if (!n.active || i == NPC.whoAmI || n.realLife != NPC.whoAmI) continue;
			n.life = 0;
			n.HitEffect();
			n.active = false;
			if (Main.netMode == NetmodeID.Server)
				NetMessage.SendData(MessageID.SyncNPC, number: i);
		}
	}
}
