#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Bosses.FallenEBF;
using GregTechCEuTerraria.TerrariaCompat.Bosses.VacuumFreezer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Mod main menu - replaces the tML/Terraria title logo with the upstream
// GregTech logo, scaled large via PointClamp. Vanilla parallax + sun/moon +
// menu buttons retained. Players switch to this via the arrows at the bottom
// of the title screen.
public class GregTechMainMenu : ModMenu
{
	private const string LogoPath = "GregTechCEuTerraria/Content/Textures/gui/icon/gregtech_logo";

	public override string DisplayName => "GregTech";
	public override Asset<Texture2D> Logo => ModContent.Request<Texture2D>(LogoPath);
	public override int Music => MusicID.Title;

	// Replace the vanilla sun with the Fallen EBF body (glowing furnace = sun),
	// and the moon with the Vacuum Freezer body (frost chamber = moon). Falls
	// through to vanilla until the runtime bake completes (first non-dedicated
	// frame in BossArt warm-up).
	public override Asset<Texture2D> SunTexture => FallenEBFRenderer.BodyAsset ?? TextureAssets.Sun;
	public override Asset<Texture2D> MoonTexture => VacuumFreezerRenderer.BodyAsset ?? TextureAssets.Moon[Terraria.Utils.Clamp(Main.moonType, 0, 8)];

	public override bool PreDrawLogo(SpriteBatch spriteBatch, ref Vector2 logoDrawCenter, ref float logoRotation, ref float logoScale, ref Color drawColor)
	{
		var logo = Logo.Value;

		// Target ~45% of screen height. Logo is 17x17 native.
		float scale = Main.screenHeight * 0.45f / logo.Height;
		var drawPos = new Vector2(Main.screenWidth / 2f, Main.screenHeight * 0.32f);

		spriteBatch.End();
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, Main.Rasterizer, null, Main.UIScaleMatrix);
		spriteBatch.Draw(logo, drawPos, null, Color.White, 0f, logo.Size() * 0.5f, scale, SpriteEffects.None, 0f);
		spriteBatch.End();
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, Main.Rasterizer, null, Main.UIScaleMatrix);

		return false; // skip vanilla logo draw
	}
}
