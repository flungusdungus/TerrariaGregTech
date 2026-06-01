#nullable enable
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;

// First-UI-frame composite of every runtime sprite so the player never sees
// the TooManyItems placeholder. Must run main-thread (Texture2D readback
// needs the GraphicsDevice); Mod.Load runs on a worker thread.
public sealed class TextureWarmUpSystem : ModSystem
{
	private bool _done;

	public override void UpdateUI(GameTime gameTime)
	{
		if (_done) return;
		_done = true;
		if (Main.dedServ) return;

		foreach (var tile in Mod.GetContent<ModTile>())
			if (tile is ITextureWarmUp w) w.WarmUpTexture();
		foreach (var item in Mod.GetContent<ModItem>())
			if (item is ITextureWarmUp w) w.WarmUpTexture();
	}
}
