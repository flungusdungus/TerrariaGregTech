#nullable enable
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;

// Draws unformed-multiblock ghost previews ABOVE the whole tile layer.
//
// Previously the ghost was painted from MetaMachineTile.PostDraw (inside the
// per-tile pass), so any wrong/placed block drawn later in the same pass
// occluded it - the ghost looked "behind" blocks. PostDrawTiles runs once,
// after every tile is drawn (and before players / NPCs / projectiles per
// ExampleMod), so the ghosts always render on top of the blocks they overlay.
public class MultiblockPreviewSystem : ModSystem
{
	public override void PostDrawTiles()
	{
		if (Main.dedServ) return;

		Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
			Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null,
			Main.GameViewMatrix.TransformationMatrix);

		MultiblockPreviewRenderer.DrawAll(Main.spriteBatch);

		Main.spriteBatch.End();
	}
}
