#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Items;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.SoulDistiller;

// Summons the Soul Distiller. A clean stainless steel machine casing run through
// an HV chemical reactor with dirt until it's caked with soul residue. Icon =
// the clean-casing texture baked darker (no custom art). Hardmode-gated,
// server-authoritative spawn via the standard tML boss-summon path.
public class DirtyStainlessSteelCasing : ModItem, ITextureWarmUp
{
	private const string CasingTex = "GregTechCEuTerraria/Content/Textures/block/casings/solid/machine_casing_clean_stainless_steel";

	public override string Texture => CasingTex;

	// Lowercase upstream-style id so a GT recipe (the HV chemical-reactor craft)
	// can reference + resolve it via IngredientResolver's ModItem fallback.
	public override string Name => "dirty_stainless_steel_casing";

	public override void SetStaticDefaults()
	{
		Language.GetOrRegister("Mods.GregTechCEuTerraria.Items.dirty_stainless_steel_casing.DisplayName", () => "Dirty Stainless Steel Casing");
		Language.GetOrRegister("Mods.GregTechCEuTerraria.Items.dirty_stainless_steel_casing.Tooltip",
			() => "A stainless casing clogged with soul residue\nUse in hardmode to summon the Soul Distiller");
		ItemID.Sets.SortingPriorityBossSpawns[Type] = 13;
	}

	// Bake the clean casing darkened + grimy.
	void ITextureWarmUp.WarmUpTexture() => ItemIconBaker.Install(Item.type,
		new IconLayer(CasingTex, new Color(96, 92, 86)));

	public override void SetDefaults()
	{
		Item.width = 28;
		Item.height = 28;
		Item.maxStack = 20;
		Item.rare = ItemRarityID.LightRed;
		Item.useAnimation = 45;
		Item.useTime = 45;
		Item.useStyle = ItemUseStyleID.HoldUp;
		Item.UseSound = SoundID.Roar;
		Item.consumable = true;
		Item.value = Item.sellPrice(silver: 60);
	}

	public override bool CanUseItem(Player player) =>
		Main.hardMode
		&& !NPC.AnyNPCs(ModContent.NPCType<SoulDistiller>())
		&& !NPC.AnyNPCs(ModContent.NPCType<SoulDistillerFraction>());

	public override bool? UseItem(Player player)
	{
		if (player.whoAmI != Main.myPlayer) return true;

		int type = ModContent.NPCType<SoulDistiller>();
		if (Main.netMode != NetmodeID.MultiplayerClient)
			NPC.SpawnOnPlayer(player.whoAmI, type);
		else
			NetMessage.SendData(MessageID.SpawnBossUseLicenseStartEvent, number: player.whoAmI, number2: type);
		return true;
	}
}
