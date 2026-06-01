#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;

// Implemented by any ModTile / ModItem whose sprite is composited at runtime
// and installed into TextureAssets (machine tiles + items, material blocks).
// TextureWarmUpSystem calls WarmUpTexture on every registered instance on the
// first frame, so the placement ghost / minimap / inventory icon never flash
// the TooManyItems placeholder before the first PreDraw installs the real
// texture. Implementations must be idempotent - the renderers' `_done` sets
// already guarantee that.
public interface ITextureWarmUp
{
	void WarmUpTexture();
}
