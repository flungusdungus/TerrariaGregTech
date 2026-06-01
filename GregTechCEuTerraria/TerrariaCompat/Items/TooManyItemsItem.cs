#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using GregTechCEuTerraria.TerrariaCompat.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items;

// Placeable for the TooManyItems debug block (creative-aid; opens global
// recipe browser anywhere).
public sealed class TooManyItemsItem : ModItem, ITextureWarmUp
{
	// PNG lives under Content/TerrariaCompat/, not next to this class.
	public override string Texture => "GregTechCEuTerraria/Content/TerrariaCompat/TooManyItemsItem";

	public void WarmUpTexture() => TooManyItemsArt.Install();

	public override void SetStaticDefaults()
	{
		Terraria.Localization.Language.GetOrRegister(
			$"Mods.GregTechCEuTerraria.Items.{Name}.DisplayName",
			() => "Too Many Items");
		Terraria.Localization.Language.GetOrRegister(
			$"Mods.GregTechCEuTerraria.Items.{Name}.Tooltip",
			() => string.Join("\n", new[]
			{
				"Place anywhere * right-click to open the global recipe browser.",
				"",
				"[c/FFE68C:Search syntax]",
				"  iron rod        - recipes containing 'iron' AND 'rod' (substring match)",
				"  platform        - finds Terraria item-name matches (e.g. plank-substituted)",
				"  @bender         - only bender recipes",
				"  @assemb         - assembler AND assembly_line (substring on station)",
				"  @bend ir pla    - bender recipes that also reference iron plate",
				"",
				"[c/FFE68C:Controls]",
				"  LMB on bar      - focus, start typing",
				"  RMB on bar      - clear search",
				"  LMB outside bar - unfocus",
				"  Esc / X         - close browser",
				"  Hover ingredient cell - tooltip with chance / tool info",
			}));
	}

	public override void SetDefaults()
	{
		Item.width  = 32;
		Item.height = 32;
		Item.maxStack = 1;
		Item.useStyle = ItemUseStyleID.Swing;
		Item.useTime = 10;
		Item.useAnimation = 15;
		Item.autoReuse = true;
		Item.consumable = true;
		Item.createTile = ModContent.TileType<Tiles.TooManyItemsTile>();
		Item.rare = ItemRarityID.Purple;
		Item.value = Item.buyPrice(silver: 1);
	}
}
