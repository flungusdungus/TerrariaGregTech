#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using GregTechCEuTerraria.TerrariaCompat.Recipes;
using GregTechCEuTerraria.TerrariaCompat.Tiles;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Creative;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items;

// Paired with SteamAgeSkipBag - drops the iron-tier crafting catalysts
// (hammer / wrench / wire cutter / file / screwdriver / saw / mortar / crowbar
// / knife) so the player has them without burning the LV bootstrap stash.
public sealed class GregTechIronToolsBag : ModItem, ITextureWarmUp
{
	public override string Texture => "GregTechCEuTerraria/Content/TerrariaCompat/TooManyItemsItem";

	public void WarmUpTexture() => StarterBagArt.InstallFor(Item.type, "gtceu:iron_wrench");

	public override void SetStaticDefaults()
	{
		Item.ResearchUnlockCount = 3;
		Terraria.Localization.Language.GetOrRegister(
			$"Mods.GregTechCEuTerraria.Items.{Name}.DisplayName",
			() => "Starter Bag: Iron Tools");
		Terraria.Localization.Language.GetOrRegister(
			$"Mods.GregTechCEuTerraria.Items.{Name}.Tooltip",
			() => "Right-click to open. Drops the iron-tier crafting catalysts:\nhammer, wrench, wire cutter, file, screwdriver, saw, mortar, crowbar, knife.");
	}

	public override void SetDefaults()
	{
		Item.width = 32;
		Item.height = 32;
		Item.maxStack = 99;
		Item.rare = ItemRarityID.Cyan;
		Item.consumable = true;
		Item.useStyle = ItemUseStyleID.HoldUp;
		Item.useTime = 20;
		Item.useAnimation = 20;
	}

	public override bool CanRightClick() => true;

	public override void RightClick(Player player)
	{
		var src = new EntitySource_Gift(player, "GregTechCEuTerraria/GregTechIronToolsBag");

		Give(src, player, "gtceu:iron_hammer",       1);
		Give(src, player, "gtceu:iron_wrench",       1);
		Give(src, player, "gtceu:iron_wire_cutter",  1);
		Give(src, player, "gtceu:iron_file",         1);
		Give(src, player, "gtceu:iron_screwdriver",  1);
		Give(src, player, "gtceu:iron_saw",          1);
		Give(src, player, "gtceu:iron_mortar",       1);
		Give(src, player, "gtceu:iron_crowbar",      1);
		Give(src, player, "gtceu:iron_knife",        1);
	}

	private static void Give(IEntitySource src, Player player, string upstreamId, int stack)
	{
		int type = IngredientResolverImpl.Instance.ResolveItemType(upstreamId);
		if (type <= 0) return;
		global::GregTechCEuTerraria.TerrariaCompat.Utils.PlayerGive.Give(player, src, type, stack);
	}
}
