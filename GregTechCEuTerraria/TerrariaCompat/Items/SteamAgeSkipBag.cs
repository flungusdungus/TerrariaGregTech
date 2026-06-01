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

// Starter bag handed with GregTechIronToolsBag (see StartingInventoryPlayer).
// RMB -> kit to jump from stone-age straight into LV-age (LV solar / battery
// buffer / hulls + components for ~5 LV machines + a stack of simple pipes).
// Iron-tier hand-craft catalysts live in GregTechIronToolsBag - the two are
// independent decisions. Item resolution through IngredientResolverImpl /
// Mod.Find; any unresolved id is silently skipped (warns on load).
public sealed class SteamAgeSkipBag : ModItem, ITextureWarmUp
{
	// Reuses the TMI logo-plate (committed PNG is the autoload fallback;
	// TooManyItemsArt.Install overwrites it at runtime).
	public override string Texture => "GregTechCEuTerraria/Content/TerrariaCompat/TooManyItemsItem";

	public void WarmUpTexture() => StarterBagArt.InstallFor(Item.type, "gtceu:steel_block");

	public override void SetStaticDefaults()
	{
		Item.ResearchUnlockCount = 3;
		Terraria.Localization.Language.GetOrRegister(
			$"Mods.GregTechCEuTerraria.Items.{Name}.DisplayName",
			() => "Starter Bag: Skip Steam Age");
		Terraria.Localization.Language.GetOrRegister(
			$"Mods.GregTechCEuTerraria.Items.{Name}.Tooltip",
			() => "Right-click to open. Drops an LV-tier bootstrap kit so you can skip the Bronze / Steam age:\n4x LV solar panel + 4x LV lamp + LV 4x battery buffer + 1 sodium battery,\n4 LV machine hulls + circuits + plates + rods + wires for assembling LV machines,\nand a stack of simple item + fluid pipes.");
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
		var src = new EntitySource_Gift(player, "GregTechCEuTerraria/SteamAgeSkipBag");

		// Minimum LV power setup.
		GiveMachine(src, player, "lv_solar_panel_machine", 4);
		GiveMachine(src, player, "lv_lamp",                4);
		GiveMachine(src, player, "lv_battery_buffer_4x",   1);
		GiveMachine(src, player, "lv_sodium_battery",      1);

		// Enough for a handful of LV machines (Macerator/Furnace/Bender/etc).
		Give(src, player, "gtceu:lv_machine_hull",        4);
		Give(src, player, "gtceu:basic_electronic_circuit", 8);
		Give(src, player, "gtceu:vacuum_tube",           16);
		Give(src, player, "gtceu:steel_ingot",           32);
		Give(src, player, "gtceu:steel_plate",           64);
		Give(src, player, "gtceu:copper_plate",          32);
		Give(src, player, "gtceu:tin_plate",             32);
		Give(src, player, "gtceu:iron_rod",              32);
		Give(src, player, "gtceu:magnetic_iron_rod",     16);
		Give(src, player, "gtceu:rubber_plate",          64);
		Give(src, player, "gtceu:tin_single_wire",       32);
		Give(src, player, "gtceu:copper_single_wire",    16);

		// Simple pipes - mod-side ModItems (not in the dump), so Mod.Find by name.
		GiveMachine(src, player, "simple_item_pipe",  100);
		GiveMachine(src, player, "simple_fluid_pipe", 100);
	}

	private static void Give(IEntitySource src, Player player, string upstreamId, int stack)
	{
		int type = IngredientResolverImpl.Instance.ResolveItemType(upstreamId);
		if (type <= 0) return;
		global::GregTechCEuTerraria.TerrariaCompat.Utils.PlayerGive.Give(player, src, type, stack);
	}

	// Machine items aren't in the resolver tables - Mod.Find by MachineKey.
	private static void GiveMachine(IEntitySource src, Player player, string machineKey, int stack)
	{
		if (!ModContent.GetInstance<GregTechCEuTerraria>().TryFind<ModItem>(machineKey, out var mi))
			return;
		global::GregTechCEuTerraria.TerrariaCompat.Utils.PlayerGive.Give(player, src, mi.Type, stack);
	}
}
