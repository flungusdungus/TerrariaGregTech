#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Items;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.FallenEBF;

// Summons the Fallen EBF. Made by over-stoking a single cupronickel coil block
// in a furnace until it can't stop burning. Icon = the cupronickel coil block
// texture composited with its bloom layer at white-hot intensity (no custom
// art). Server-authoritative spawn via the standard tML boss-summon path.
public class OverburnedCoilBlock : ModItem, ITextureWarmUp
{
	private const string CoilBase  = "GregTechCEuTerraria/Content/Textures/block/casings/coils/machine_coil_cupronickel";
	private const string CoilBloom = "GregTechCEuTerraria/Content/Textures/block/casings/coils/machine_coil_cupronickel_bloom";

	// Autoload placeholder; WarmUpTexture bakes the burning composite over it.
	public override string Texture => CoilBase;

	public override void SetStaticDefaults()
	{
		Language.GetOrRegister("Mods.GregTechCEuTerraria.Items.OverburnedCoilBlock.DisplayName", () => "Overburned Coil Block");
		Language.GetOrRegister("Mods.GregTechCEuTerraria.Items.OverburnedCoilBlock.Tooltip",
			() => "A cupronickel coil block stoked past its limit\nUse on the surface to summon the Fallen EBF");
		ItemID.Sets.SortingPriorityBossSpawns[Type] = 12;
	}

	// Bake "cupronickel coil block, burning a lot": hot-tinted base + the bloom
	// layer stacked (white + an extra orange pass) so the coils read molten.
	void ITextureWarmUp.WarmUpTexture() => ItemIconBaker.Install(Item.type,
		new IconLayer(CoilBase, new Color(255, 160, 110)),
		new IconLayer(CoilBloom, Color.White),
		new IconLayer(CoilBloom, new Color(255, 120, 40)));

	public override void SetDefaults()
	{
		Item.width = 28;
		Item.height = 28;
		Item.maxStack = 20;
		Item.rare = ItemRarityID.Orange;
		Item.useAnimation = 45;
		Item.useTime = 45;
		Item.useStyle = ItemUseStyleID.HoldUp;
		Item.UseSound = SoundID.Roar;
		Item.consumable = true;
		Item.value = Item.sellPrice(silver: 80);
	}

	// The server uses this same check when it receives the spawn message.
	public override bool CanUseItem(Player player) => !NPC.AnyNPCs(ModContent.NPCType<FallenEBF>());

	public override bool? UseItem(Player player)
	{
		if (player.whoAmI == Main.myPlayer)
		{
			int type = ModContent.NPCType<FallenEBF>();
			if (Main.netMode != NetmodeID.MultiplayerClient)
				NPC.SpawnOnPlayer(player.whoAmI, type);
			else
				NetMessage.SendData(MessageID.SpawnBossUseLicenseStartEvent, number: player.whoAmI, number2: type);
		}
		return true;
	}

	public override void AddRecipes()
	{
		// 1 cupronickel coil block, smelted at a furnace.
		if (!Mod.TryFind<ModItem>("cupronickel_coil_block", out var coil))
		{
			Mod.Logger.Warn("[FallenEBF] OverburnedCoilBlock recipe skipped: cupronickel_coil_block not found.");
			return;
		}

		CreateRecipe()
			.AddIngredient(coil.Type, 1)
			.AddTile(TileID.Furnaces)
			.Register();
	}
}
