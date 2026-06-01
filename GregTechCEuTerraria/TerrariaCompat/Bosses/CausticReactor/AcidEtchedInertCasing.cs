#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Items;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.CausticReactor;

// Summons the Caustic Reactor. Made by running a chemically-inert (PTFE) machine
// casing through an HV chemical reactor with sulfuric acid until its surface is
// etched and reactive (see the chemical_reactor recipe in CompatRecipes). Icon =
// the inert casing texture tinted acid-green. Server-authoritative spawn via the
// standard tML boss-summon path. Mirrors FrozenFrostproofCasing (Vacuum Freezer).
public class AcidEtchedInertCasing : ModItem, ITextureWarmUp
{
	private const string CasingTex = "GregTechCEuTerraria/Content/Textures/block/casings/solid/machine_casing_inert_ptfe";

	public override string Texture => CasingTex;

	// Lowercase upstream-style id so the chemical_reactor GT recipe can reference +
	// resolve it via IngredientResolver's ModItem fallback (gtceu:acid_etched_inert_casing).
	public override string Name => "acid_etched_inert_casing";

	public override void SetStaticDefaults()
	{
		Language.GetOrRegister("Mods.GregTechCEuTerraria.Items.acid_etched_inert_casing.DisplayName", () => "Acid-Etched Inert Casing");
		Language.GetOrRegister("Mods.GregTechCEuTerraria.Items.acid_etched_inert_casing.Tooltip",
			() => "An inert casing etched reactive in an HV chemical reactor\nUse on the surface to summon the Caustic Reactor");
		ItemID.Sets.SortingPriorityBossSpawns[Type] = 13;
	}

	// Inert casing tinted corrosive-green (the baker's tint is multiply-only).
	void ITextureWarmUp.WarmUpTexture() => ItemIconBaker.Install(Item.type,
		new IconLayer(CasingTex, new Color(150, 215, 70)));

	public override void SetDefaults()
	{
		Item.width = 28;
		Item.height = 28;
		Item.maxStack = 20;
		Item.rare = ItemRarityID.Lime;
		Item.useAnimation = 45;
		Item.useTime = 45;
		Item.useStyle = ItemUseStyleID.HoldUp;
		Item.UseSound = SoundID.Item8; // hiss
		Item.consumable = true;
		Item.value = Item.sellPrice(silver: 90);
	}

	public override bool CanUseItem(Player player) => !NPC.AnyNPCs(ModContent.NPCType<CausticReactor>());

	public override bool? UseItem(Player player)
	{
		if (player.whoAmI != Main.myPlayer) return true;

		int type = ModContent.NPCType<CausticReactor>();
		if (Main.netMode != NetmodeID.MultiplayerClient)
			NPC.SpawnOnPlayer(player.whoAmI, type);
		else
			NetMessage.SendData(MessageID.SpawnBossUseLicenseStartEvent, number: player.whoAmI, number2: type);
		return true;
	}
}
