#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items;

// Port of upstream gtceu:terminal (ComponentItem + TerminalBehavior). Bare id
// "terminal" matches the dump, so RegistryItemLoader skips it (Mod.TryFind wins)
// and the upstream crafting recipe resolves to this ModItem.
//
// DEVIATION: plain RMB instead of upstream's shift+RMB; intercepted
// in TieredMachineTile.RightClick before GUI-open. Auto-build path is in
// MultiblockAutoBuilder (port of BlockPattern.autoBuild).
public sealed class TerminalItem : ModItem, ITextureWarmUp
{
	public override string Name => "terminal";

	public override string Texture => "GregTechCEuTerraria/Content/Textures/item/terminal";

	public override void SetStaticDefaults()
	{
		Language.GetOrRegister("Mods.GregTechCEuTerraria.Items.terminal.DisplayName", () => "Terminal");

		if (Main.dedServ) return;

		// Vertical-strip animation (mcmeta MC ticks -> x3 for Terraria 60 Hz).
		var tex = ModContent.Request<Texture2D>(Texture, AssetRequestMode.ImmediateLoad).Value;
		if (tex.Width > 0 && tex.Height > tex.Width && tex.Height % tex.Width == 0)
		{
			int frames = tex.Height / tex.Width;
			var (frameTime, _) = McMeta.Read(Mod, "Content/Textures/item/terminal.png.mcmeta");
			Main.RegisterItemAnimation(Type, new DrawAnimationVertical(frameTime * 3, frames));
		}
	}

	public override void SetDefaults()
	{
		Item.maxStack = 1;
		Item.width = 32;
		Item.height = 32;
		Item.useStyle = ItemUseStyleID.None;
		Item.value = Item.buyPrice(gold: 1);
		Item.rare = ItemRarityID.Cyan;
	}

	// Generated en-US.hjson ships an empty `terminal.Tooltip` so GetOrRegister
	// can't fill it; inject the usage line via ModifyTooltips instead.
	public override void ModifyTooltips(List<TooltipLine> tooltips)
	{
		tooltips.Add(new TooltipLine(Mod, "TerminalUsage",
			"Right-click an unformed multiblock controller to auto-build its " +
			"structure from blocks in your inventory"));
	}

	public override void HoldItem(Player player)
	{
		base.HoldItem(player);
		ItemIconBaker.Install(Item.type, Texture);
	}

	void ITextureWarmUp.WarmUpTexture() => ItemIconBaker.Install(Item.type, Texture);

	// Called from TieredMachineTile.RightClick BEFORE GUI-open. Build is
	// client-authoritative for tiles+inventory; entity creation rides
	// MachinePlacedPacket - see MultiblockAutoBuilder. Returns false -> GUI opens.
	public static bool TryAutoBuild(MetaMachine machine, Player player)
	{
		if (machine is not MultiblockControllerMachine controller) return false;
		if (controller.IsFormed) return false;
		if (player.HeldItem is not { } held || held.IsAir) return false;
		if (held.type != ModContent.ItemType<TerminalItem>()) return false;

		MultiblockAutoBuilder.Build(controller, player);
		return true;
	}
}
