#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Items;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.ImplosionPress;

// Summons the Implosion Press. Crafted by running a solid-steel machine casing
// through a Large Chemical Reactor with ITNT slurry + lubricant (see the
// large_chemical_reactor recipe in CompatRecipes). Icon = the solid-steel casing
// tinted ITNT-red so it reads "unstable" at a glance. Mirrors AcidEtchedInertCasing
// (Caustic Reactor) and FrozenFrostproofCasing (Vacuum Freezer).
public class UnstableCompressorCharge : ModItem, ITextureWarmUp
{
	private const string CasingTex = "GregTechCEuTerraria/Content/Textures/block/casings/solid/machine_casing_solid_steel";

	public override string Texture => CasingTex;

	// Lowercase upstream-style id so the large_chemical_reactor recipe can
	// reference + resolve it via IngredientResolver's ModItem fallback
	// (gtceu:unstable_compressor_charge).
	public override string Name => "unstable_compressor_charge";

	public override void SetStaticDefaults()
	{
		Language.GetOrRegister("Mods.GregTechCEuTerraria.Items.unstable_compressor_charge.DisplayName",
			() => "Unstable Compressor Charge");
		Language.GetOrRegister("Mods.GregTechCEuTerraria.Items.unstable_compressor_charge.Tooltip",
			() => "A steel casing packed with ITNT, primed in a Large Chemical Reactor\nUse on the surface to summon the Implosion Press");
		ItemID.Sets.SortingPriorityBossSpawns[Type] = 14;
	}

	// Solid-steel casing tinted ITNT-red (multiply-only tint - the casing's value
	// detail survives, just shifts toward red-warning).
	void ITextureWarmUp.WarmUpTexture() => ItemIconBaker.Install(Item.type,
		new IconLayer(CasingTex, new Color(245, 95, 75)));

	public override void SetDefaults()
	{
		Item.width = 28;
		Item.height = 28;
		Item.maxStack = 20;
		Item.rare = ItemRarityID.Yellow;
		Item.useAnimation = 45;
		Item.useTime = 45;
		Item.useStyle = ItemUseStyleID.HoldUp;
		Item.UseSound = SoundID.Item62; // bomb fuse hiss
		Item.consumable = true;
		Item.value = Item.sellPrice(gold: 1, silver: 50);
	}

	public override bool CanUseItem(Player player) => !NPC.AnyNPCs(ModContent.NPCType<ImplosionPress>());

	public override bool? UseItem(Player player)
	{
		if (player.whoAmI != Main.myPlayer) return true;

		int type = ModContent.NPCType<ImplosionPress>();
		if (Main.netMode != NetmodeID.MultiplayerClient)
			NPC.SpawnOnPlayer(player.whoAmI, type);
		else
			NetMessage.SendData(MessageID.SpawnBossUseLicenseStartEvent, number: player.whoAmI, number2: type);
		return true;
	}
}
