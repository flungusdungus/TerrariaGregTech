#nullable enable
using GregTechCEuTerraria.TerrariaCompat.BossDrops.MultiblockBag;
using GregTechCEuTerraria.TerrariaCompat.Items.Pipes;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using GregTechCEuTerraria.TerrariaCompat.Recipes;
using GregTechCEuTerraria.TerrariaCompat.Tiles;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items;

// Pairs with SteamAgeSkipBag - steam-age auto-mining setup so the player can
// skip every pickaxe swing. HP miner + two HP coal boilers + coke + bronze
// crates + steam furnace + macerator + pipes + primitive-pump / coke-oven
// multiblock bags.
public sealed class SkipStoneAgeBag : ModItem, ITextureWarmUp
{
	public override string Texture => "GregTechCEuTerraria/Content/TerrariaCompat/TooManyItemsItem";

	public void WarmUpTexture() => StarterBagArt.InstallFor(Item.type, "gtceu:bronze_block");

	public override void SetStaticDefaults()
	{
		Item.ResearchUnlockCount = 3;
		Terraria.Localization.Language.GetOrRegister(
			$"Mods.GregTechCEuTerraria.Items.{Name}.DisplayName",
			() => "Starter Bag: Skip Stone Age");
		Terraria.Localization.Language.GetOrRegister(
			$"Mods.GregTechCEuTerraria.Items.{Name}.Tooltip",
			() => "Right-click to open. Drops a steam-age auto-mining setup so you can skip manual mining:\nHP steam miner, two HP coal boilers, bronze crates, HP steam furnace + macerator,\ncoke fuel, fluid + item pipes, and the primitive-pump + coke-oven multiblock bags.");
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
		var src = new EntitySource_Gift(player, "GregTechCEuTerraria/SkipStoneAgeBag");

		GiveMachine(src, player, "hp_steam_miner",        1);
		GiveMachine(src, player, "hp_steam_solid_boiler", 2);
		GiveMachine(src, player, "bronze_crate",          2);
		GiveMachine(src, player, "hp_steam_furnace",      1);
		GiveMachine(src, player, "hp_steam_macerator",    1);

		// Coke = the `coke` material's `gem` form.
		Give(src, player, "gtceu:coke_gem", 100);

		// PipeItemRegistry isn't on the resolver path.
		GivePipe(src, player, "steel_tiny_fluid_pipe", 100);
		GivePipe(src, player, "tin_small_item_pipe",   100);

		GiveBag(src, player, "primitive_pump", 1);
		GiveBag(src, player, "coke_oven",      1);
	}

	private static void Give(IEntitySource src, Player player, string upstreamId, int stack)
	{
		int type = IngredientResolverImpl.Instance.ResolveItemType(upstreamId);
		if (type <= 0) return;
		global::GregTechCEuTerraria.TerrariaCompat.Utils.PlayerGive.Give(player, src, type, stack);
	}

	private static void GiveMachine(IEntitySource src, Player player, string machineKey, int stack)
	{
		if (!ModContent.GetInstance<GregTechCEuTerraria>().TryFind<ModItem>(machineKey, out var mi))
			return;
		global::GregTechCEuTerraria.TerrariaCompat.Utils.PlayerGive.Give(player, src, mi.Type, stack);
	}

	private static void GivePipe(IEntitySource src, Player player, string bareId, int stack)
	{
		int? type = PipeItemRegistry.Get(bareId);
		if (type is null) return;
		global::GregTechCEuTerraria.TerrariaCompat.Utils.PlayerGive.Give(player, src, type.Value, stack);
	}

	private static void GiveBag(IEntitySource src, Player player, string multiId, int stack)
	{
		if (!MultiblockBagLoader.TryGet(multiId, out int type)) return;
		global::GregTechCEuTerraria.TerrariaCompat.Utils.PlayerGive.Give(player, src, type, stack);
	}
}
