#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Items;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.VacuumFreezer;

// Summons the Vacuum Freezer. Made by running a frost-proof machine casing
// through an HV chemical bath with distilled water until it's frozen solid (see
// the chemical_bath recipe in CompatRecipes). Icon = the plain frost-proof
// casing texture (no custom art). Server-authoritative spawn via the standard
// tML boss-summon path.
public class FrozenFrostproofCasing : ModItem, ITextureWarmUp
{
	private const string CasingTex = "GregTechCEuTerraria/Content/Textures/block/casings/solid/machine_casing_frost_proof";

	// Autoload placeholder; WarmUpTexture bakes the (upscaled) casing over it.
	public override string Texture => CasingTex;

	// Lowercase upstream-style id so the chemical_bath GT recipe can reference +
	// resolve it via IngredientResolver's ModItem fallback (gtceu:frozen_frostproof_casing).
	public override string Name => "frozen_frostproof_casing";

	public override void SetStaticDefaults()
	{
		Language.GetOrRegister("Mods.GregTechCEuTerraria.Items.frozen_frostproof_casing.DisplayName", () => "Frozen Frostproof Casing");
		Language.GetOrRegister("Mods.GregTechCEuTerraria.Items.frozen_frostproof_casing.Tooltip",
			() => "A frost-proof casing frozen solid in an HV chemical bath\nUse on the surface to summon the Vacuum Freezer");
		ItemID.Sets.SortingPriorityBossSpawns[Type] = 13;
	}

	// Frost-proof casing tinted icy-blue (the baker's tint is multiply-only, so
	// it can't brighten - a blue cast reads as "frozen solid" instead).
	void ITextureWarmUp.WarmUpTexture() => ItemIconBaker.Install(Item.type,
		new IconLayer(CasingTex, new Color(105, 165, 255)));

	public override void SetDefaults()
	{
		Item.width = 28;
		Item.height = 28;
		Item.maxStack = 20;
		Item.rare = ItemRarityID.LightPurple;
		Item.useAnimation = 45;
		Item.useTime = 45;
		Item.useStyle = ItemUseStyleID.HoldUp;
		Item.UseSound = SoundID.Item30; // icy crack
		Item.consumable = true;
		Item.value = Item.sellPrice(silver: 80);
	}

	// The server uses this same check when it receives the spawn message.
	public override bool CanUseItem(Player player) => !NPC.AnyNPCs(ModContent.NPCType<VacuumFreezer>());

	public override bool? UseItem(Player player)
	{
		if (player.whoAmI != Main.myPlayer) return true;

		int type = ModContent.NPCType<VacuumFreezer>();
		if (Main.netMode != NetmodeID.MultiplayerClient)
			NPC.SpawnOnPlayer(player.whoAmI, type);
		else
			NetMessage.SendData(MessageID.SpawnBossUseLicenseStartEvent, number: player.whoAmI, number2: type);
		return true;
	}
}
