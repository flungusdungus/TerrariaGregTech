#nullable enable
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.Common;

// Swaps a boss's load-time placeholder head texture for a runtime-baked one.
//
// Usage on a ModNPC that bakes its own head:
//   1. annotate the class with [AutoloadBossHead] and override BossHeadTexture
//      to point at ANY existing asset (the placeholder) - this registers the
//      head slot and enables the bottom HP bar + minimap/off-screen icon;
//   2. call SwapBakedHead(NPC, bakedAsset, ref _swapped) once from PreDraw to
//      replace the placeholder with the real baked head.
public static class BossHeadHelper
{
	public static void SwapBakedHead(NPC npc, Asset<Texture2D>? baked, ref bool swapped)
	{
		if (swapped) return;
		swapped = true;
		if (baked is null) return;
		int slot = npc.GetBossHeadTextureIndex();
		if (slot >= 0 && slot < TextureAssets.NpcHeadBoss.Length)
			TextureAssets.NpcHeadBoss[slot] = baked;
	}
}
