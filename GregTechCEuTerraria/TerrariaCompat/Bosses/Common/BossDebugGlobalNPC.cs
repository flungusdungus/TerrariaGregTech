#nullable enable
using GregTechCEuTerraria.Config;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.Common;

// Routes the per-frame IDebuggableBoss.DrawDebugGizmos call. Runs in entity
// sprite space (Main.spriteBatch is already started by the NPC draw pass), so
// gizmos use world-coords minus Main.screenPosition to land on the boss.
//
// Gated on GTConfig.DebugMobs; zero cost when off.
public class BossDebugGlobalNPC : GlobalNPC
{
	public override bool InstancePerEntity => false;

	public override void PostDraw(NPC npc, SpriteBatch sb, Vector2 screenPos, Color drawColor)
	{
		if (!GTConfig.Instance.DebugMobs) return;
		if (npc.ModNPC is IDebuggableBoss debug)
			debug.DrawDebugGizmos(sb, screenPos);
	}
}
