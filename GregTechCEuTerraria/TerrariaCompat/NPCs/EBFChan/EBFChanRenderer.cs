#nullable enable
using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.NPCs.EBFChan;

// Draws EBF-chan as a fully layered Terraria character (hair / armor / accessories
// / dyes) instead of a flat NPC sprite, using the same player-render pipeline the
// game uses for mannequins and the player themselves. Same technique as Calamity's
// "player clone" projectile: configure a throwaway Player, run the display-doll
// update chain, drive the walk/idle frames by hand, then Main.PlayerRenderer.DrawPlayer.
//
// One shared clone is fine - draws happen sequentially on the main thread and we
// fully reconfigure it per call.
public static class EBFChanRenderer
{
	private static Player? _clone;

	// Player sprite frames are 40x56; standing = frame 0, the walk cycle is
	// frames 6..19 (14 frames). Tunable foot-alignment offset.
	private const int FrameHeight = 56;
	private const int WalkFirstFrame = 6;
	private const int WalkFrameCount = 14;
	private const float DrawScale = 1f;
	private const float FootOffsetY = 0f; // nudge so feet sit on NPC.Bottom (tune if needed)

	public static void Draw(NPC npc, Vector2 screenPosUnused, Color drawColor)
	{
		Player clone = _clone ??= MakeClone();

		EBFChanAppearance.Apply(clone);

		// Display-doll update chain - registers visible accessories + dye shaders
		// and frames the body. Mirrors Calamity's DarkMasterClone.
		clone.ResetEffects();
		clone.ResetVisibleAccessories();
		clone.DisplayDollUpdate();
		clone.UpdateSocialShadow();
		clone.UpdateDyes();
		clone.PlayerFrame();

		// Facing: town NPCs set spriteDirection; fall back to movement direction.
		clone.direction = npc.spriteDirection != 0 ? npc.spriteDirection : (npc.direction != 0 ? npc.direction : 1);

		// Walk / idle animation, driven off NPC velocity. NPC.frameCounter is our
		// accumulator (town NPC AI doesn't otherwise use it once PreDraw owns draw).
		bool moving = Math.Abs(npc.velocity.X) > 0.1f;
		int frameY;
		if (moving)
		{
			npc.frameCounter += 1.0 + Math.Abs(npc.velocity.X) * 0.4;
			int wf = WalkFirstFrame + (int)(npc.frameCounter / 6.0) % WalkFrameCount;
			frameY = wf * FrameHeight;
		}
		else
		{
			npc.frameCounter = 0;
			frameY = 0;
		}
		clone.bodyFrame.Y = frameY;
		clone.legFrame.Y = frameY;
		clone.headFrame.Y = 0;

		// World draw position: align the player's feet with the NPC's bottom and
		// centre horizontally. DrawPlayer handles the screen offset internally
		// (pass world coords), same as DarkMasterClone passing Projectile.position.
		var pos = new Vector2(
			npc.Center.X - clone.width / 2f,
			npc.Bottom.Y - clone.height - FootOffsetY);
		clone.position = pos;

		Main.PlayerRenderer.DrawPlayer(Main.Camera, clone, pos, 0f, Vector2.Zero, 0f, DrawScale);
	}

	private static Player MakeClone()
	{
		var p = new Player { active = true, name = "EBFChan" };
		// Empty every equip/dye slot up front; Apply() fills from the baked data.
		for (int i = 0; i < p.armor.Length; i++) p.armor[i].TurnToAir();
		for (int i = 0; i < p.dye.Length; i++) p.dye[i].TurnToAir();
		return p;
	}
}
