#nullable enable
using System.Collections.Generic;
using System.Linq;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Common.Materials;
using GregTechCEuTerraria.TerrariaCompat.Bosses.FallenEBF;
using GregTechCEuTerraria.TerrariaCompat.Items.Fluids;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.Bestiary;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.Utilities;

namespace GregTechCEuTerraria.TerrariaCompat.NPCs.EBFChan;

// EBF-chan - the first hand-authored town NPC. She arrives (wanders the surface,
// then moves into housing) once the Fallen EBF has been defeated, and sells
// tier-relevant LV-age materials to smooth the EBF supply chain.
//
// Visuals are NOT a flat NPC spritesheet: PreDraw routes to EBFChanRenderer, which
// draws her as a fully layered Terraria character (hair/armor/accessories/dyes)
// baked from a player the user designs (see EBFChanAppearance + /ebfchan_export).
//
// No [AutoloadHead] - that requires a "<Texture>_Head" PNG we don't have art for.
// Instead we register a placeholder head from an existing PNG in Load(); swap to a
// real baked player-head later.
public class EBFChanNPC : ModNPC
{
	public const string ShopName = "Shop";

	private static int _headSlot;

	// ModNPC needs a real autoloadable texture even though PreDraw draws the
	// mannequin and returns false. Reuse an existing upstream PNG (matches FallenEBF).
	public override string Texture => "GregTechCEuTerraria/Content/Textures/block/casings/solid/machine_casing_heatproof";

	public override void Load()
	{
		// Placeholder head for the housing menu + map icon. TODO: bake a real
		// player-head from the mannequin (Main.PlayerRenderer.DrawPlayerHead).
		_headSlot = Mod.AddNPCHeadTexture(Type, Texture);
	}

	public override void SetStaticDefaults()
	{
		// We draw EBF-chan as a layered player mannequin (PreDraw returns false),
		// so the NPC's own spritesheet is never shown. frameCount MUST be 1: the
		// dummy casing texture is only ~16px tall, and vanilla town framing computes
		// frameHeight = textureHeight / npcFrameCount then DIVIDES by it - any value
		// that makes frameHeight 0 throws DivideByZero in VanillaFindFrame every tick.
		Main.npcFrameCount[Type] = 1;

		NPCID.Sets.HatOffsetY[Type] = 4;

		NPCID.Sets.NPCBestiaryDrawModifiers drawModifiers = new() { Velocity = 1f, Direction = 1 };
		NPCID.Sets.NPCBestiaryDrawOffset.Add(Type, drawModifiers);

		// DisplayName MUST be keyed by the class name (EBFChanNPC) - that's the key
		// tML reads for NPC.TypeName/FullName. Keying it "EBFChan" left her name as
		// the auto-split class name ("E B F Chan N P C").
		Language.GetOrRegister("Mods.GregTechCEuTerraria.NPCs.EBFChanNPC.DisplayName", () => "EBF-chan");
		Language.GetOrRegister("Mods.GregTechCEuTerraria.NPCs.EBFChan.Bestiary",
			() => "The spirit of a Electric Blast Furnace, settled and friendly now that her Fallen shell is dealt with. She runs a little stall of heat-treated ingredients - and is always hot to the touch.");
		Language.GetOrRegister("Mods.GregTechCEuTerraria.NPCs.EBFChan.DeathMessage", () => "{0} let her coils go cold...");
		Language.GetOrRegister("Mods.GregTechCEuTerraria.NPCs.EBFChan.Chat1", () => "Hold on, I'm still cooking your kanthal ingot...");
		Language.GetOrRegister("Mods.GregTechCEuTerraria.NPCs.EBFChan.Chat2", () => "Don't touch the casing. It's hot. Everything's hot.");
		Language.GetOrRegister("Mods.GregTechCEuTerraria.NPCs.EBFChan.Chat3", () => "Overclock responsibly. I've seen what happens when you don't.");
	}

	public override void SetDefaults()
	{
		NPC.townNPC = true;
		NPC.friendly = true;
		// Generous box so melee swings / thrown blades connect and collision feels
		// solid (the vanilla 18x40 felt too thin under the mannequin). The renderer
		// centres the ~20x42 player horizontally and aligns its feet to NPC.Bottom
		// inside this box.
		NPC.width = 40;
		NPC.height = 52;
		NPC.aiStyle = NPCAIStyleID.Passive;
		NPC.damage = 10;
		NPC.defense = 15;
		NPC.lifeMax = 250;
		NPC.HitSound = SoundID.NPCHit4;     // metallic clang, like the boss
		NPC.DeathSound = SoundID.NPCDeath14;
		NPC.knockBackResist = 0.5f;

		AnimationType = NPCID.Guide;
	}

	public override LocalizedText DeathMessage => Language.GetText("Mods.GregTechCEuTerraria.NPCs.EBFChan.DeathMessage");

	public override void SetBestiary(BestiaryDatabase database, BestiaryEntry bestiaryEntry)
	{
		bestiaryEntry.Info.AddRange(new IBestiaryInfoElement[]
		{
			BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Biomes.Surface,
			new FlavorTextBestiaryInfoElement("Mods.GregTechCEuTerraria.NPCs.EBFChan.Bestiary"),
		});
	}

	// Gate: she only exists once the Fallen EBF is down. The surface-wandering
	// spawn is handled by EBFChanSpawnSystem; this controls the normal "move into
	// a vacant house" auto-spawn so housing works the usual way too.
	public override bool CanTownNPCSpawn(int numTownNPCs) => FallenEBFWorld.Downed;

	public override ITownNPCProfile TownNPCProfile() => new Profiles.DefaultNPCProfile(Texture, _headSlot);

	// Town NPCs get a random "given name" from this list (shown as the NPC's name,
	// e.g. the Guide's "Andrew"). She should always just be EBF-chan.
	public override List<string> SetNPCNameList() => new() { "EBF-chan" };

	// Draw her as a layered player mannequin instead of the flat NPC sprite. In the
	// bestiary the NPC is an off-world dummy, so DrawPlayer's world-space math would
	// mis-place her - fall back to the vanilla dummy texture there for now.
	public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
	{
		if (NPC.IsABestiaryIconDummy)
			return true;

		EBFChanRenderer.Draw(NPC, screenPos, drawColor);
		return false;
	}

	public override string GetChat()
	{
		WeightedRandom<string> chat = new();
		chat.Add(Language.GetTextValue("Mods.GregTechCEuTerraria.NPCs.EBFChan.Chat1"));
		chat.Add(Language.GetTextValue("Mods.GregTechCEuTerraria.NPCs.EBFChan.Chat2"));
		chat.Add(Language.GetTextValue("Mods.GregTechCEuTerraria.NPCs.EBFChan.Chat3"));
		return chat;
	}

	public override void SetChatButtons(ref string button, ref string button2)
	{
		button = Language.GetTextValue("LegacyInterface.28"); // "Shop"
	}

	public override void OnChatButtonClicked(bool firstButton, ref string shop)
	{
		if (firstButton)
			shop = ShopName;
	}

	public override void AddShops()
	{
		var shop = new NPCShop(Type, ShopName);

		// Expensive hydrogen gas cell (a filled fluid_cell).
		AddHydrogenCell(shop, Item.buyPrice(gold: 2));

		// Cheap early-game metal dusts.
		AddIfPresent(shop, "aluminium_dust", Item.buyPrice(silver: 20));
		AddIfPresent(shop, "iron_dust", Item.buyPrice(silver: 8));
		AddIfPresent(shop, "copper_dust", Item.buyPrice(silver: 8));
		AddIfPresent(shop, "tin_dust", Item.buyPrice(silver: 8));
		AddIfPresent(shop, "nickel_dust", Item.buyPrice(silver: 12));
		AddIfPresent(shop, "lead_dust", Item.buyPrice(silver: 10));

		// VERY expensive finished ingots.
		AddIfPresent(shop, "steel_ingot", Item.buyPrice(gold: 25));
		AddIfPresent(shop, "aluminium_ingot", Item.buyPrice(gold: 35));
		AddIfPresent(shop, "kanthal_ingot", Item.buyPrice(gold: 50));

		shop.Register();
	}

	// A filled hydrogen cell: the basic fluid_cell with hydrogen in its NBT. We pull
	// the fluid straight off the hydrogen material (its primary fluid) rather than
	// guessing the registry id - a GAS-primary material may register as "hydrogen"
	// OR "hydrogen_gas" depending on PrimaryKey, and TryGet on the wrong id silently
	// leaves the cell empty. Skips gracefully if the cell item or fluid is missing.
	private void AddHydrogenCell(NPCShop shop, int priceCopper)
	{
		if (!Mod.TryFind<ModItem>("fluid_cell", out var cellMi)) return;
		var it = new Item(cellMi.Type);
		FluidType? hydrogen = MaterialRegistry.All.TryGetValue("hydrogen", out var mat)
			? mat.FluidProperty?.Get() ?? mat.FluidProperty?.Fluids.FirstOrDefault()
			: null;
		if (it.ModItem is FluidCellItem cell && hydrogen != null)
			cell.Fill(new FluidStack(hydrogen, cell.Capacity), simulate: false);
		it.shopCustomPrice = priceCopper;
		shop.Add(it);
	}

	private void AddIfPresent(NPCShop shop, string itemId, int priceCopper)
	{
		if (Mod.TryFind<ModItem>(itemId, out var mi))
			shop.Add(new Item(mi.Type) { shopCustomPrice = priceCopper });
	}
}
