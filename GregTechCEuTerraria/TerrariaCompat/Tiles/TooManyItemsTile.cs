#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using GregTechCEuTerraria.TerrariaCompat.UI;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles;

// 2x2 placeable debug block. RMB opens the global recipe browser - a
// JEI-style UI showing recipes across every station with @station search
// filtering. Deliberately styled as the Source-engine 404 magenta/black
// checkerboard so it reads at a glance as "creative-mode utility, not a
// real machine".
public sealed class TooManyItemsTile : ModTile, ITextureWarmUp
{
	// Bake the GregTech-logo tile sheet on the first main-thread frame -
	// see TextureWarmUpSystem / ITextureWarmUp.
	public void WarmUpTexture() => TooManyItemsArt.Install();

	// Explicit - the PNG lives under Content/TerrariaCompat/, not next to this
	// class, so tML's namespace-path auto-resolution would miss it.
	public override string Texture => "GregTechCEuTerraria/Content/TerrariaCompat/TooManyItemsTile";

	public override void SetStaticDefaults()
	{
		Main.tileFrameImportant[Type] = true;
		Main.tileNoAttach[Type]       = false;
		Main.tileLavaDeath[Type]      = false;
		Main.tileSolid[Type]          = false;       // walk-through (consistent with other GT machines)
		Main.tileSolidTop[Type]       = true;        // stand-on-top
		Main.tileTable[Type]          = false;
		Main.tileBlockLight[Type]     = false;

		TileObjectData.newTile.CopyFrom(TileObjectData.Style2x2);
		// Style2x2 already has SolidBottom anchor + UsesCustomCanPlace - no
		// extra anchor override needed (would just pin us to Terraria.Enums).
		// Origin (1, 1) - center-of-cursor placement, matches every other
		// 2x2 in the codebase (MetaMachineTile / MaterialBlockTile / CasingTile).
		TileObjectData.newTile.Origin = new Point16(1, 1);
		TileObjectData.addTile(Type);
		Players.CenteredPlacementPlayer.CenteredPlacementTiles.Add(Type);

		AddMapEntry(new Color(180, 50, 200), Terraria.Localization.Language.GetOrRegister(
			$"Mods.GregTechCEuTerraria.Tiles.{Name}.MapEntry", () => "Too Many Items"));

		DustType = DustID.Stone;
		MineResist = 0.5f;
		MinPick = 0;
	}

	public override bool RightClick(int i, int j)
	{
		// Open the global recipe browser. Plays the standard chest-style open
		// click so the player gets feedback the action registered.
		SoundEngine.PlaySound(SoundID.MenuTick);
		GlobalRecipeBrowserSystem.Open();
		return true;
	}

	public override void MouseOver(int i, int j)
	{
		Player player = Main.LocalPlayer;
		player.cursorItemIconEnabled = true;
		player.cursorItemIconID = ModContent.ItemType<Items.TooManyItemsItem>();
	}
}
