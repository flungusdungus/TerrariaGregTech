#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Machine;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using GregTechCEuTerraria.TerrariaCompat.Net;
using GregTechCEuTerraria.TerrariaCompat.Pipelike;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Pipes;

// Early-game item pipe with a simplified 3-state per-side UI. Sentinel
// MaterialId "simple_item" separates its network from real material pipes.
// Stats sit between tin and copper - out-classed past the steam age.
public sealed class SimpleItemPipeItem : ModItem, ITextureWarmUp
{
	public override string Name     => "simple_item_pipe";
	public override string Texture  => "GregTechCEuTerraria/Content/Textures/block/pipe/pipe_normal_in";
	protected override bool CloneNewInstances => true;

	public override void SetStaticDefaults()
	{
		Language.GetOrRegister(
			$"Mods.GregTechCEuTerraria.Items.{Name}.DisplayName",
			() => "Simple Item Pipe");
	}

	public override void SetDefaults()
	{
		Item.maxStack = 999;
		Item.width = 32; Item.height = 32;
		Item.useTime = 8; Item.useAnimation = 8;
		Item.useStyle = ItemUseStyleID.Swing;
		Item.autoReuse = true;
		Item.consumable = false;
		Item.rare = ItemRarityID.White;
		Item.UseSound = SoundID.Item50;
	}

	public override void ModifyTooltips(List<TooltipLine> tooltips)
	{
		base.ModifyTooltips(tooltips);
		tooltips.Add(new TooltipLine(Mod, "PipeKind", "Simple Item Pipe"));
		tooltips.Add(new TooltipLine(Mod, "PipeRate", "[c/55FFFF:Transfer Rate:] 16 items/s"));
		tooltips.Add(new TooltipLine(Mod, "PipeSimple",
			"[c/AAFFAA:Auto-connects to adjacent storage on placement.]"));
		tooltips.Add(new TooltipLine(Mod, "PipeSimpleUI",
			"[c/AAFFAA:Right-click to toggle per-side mode (Off / Insert / Extract).]"));
	}

	public override bool? UseItem(Player player)
	{
		if (Main.myPlayer != player.whoAmI) return null;
		int x = (int)(Main.MouseWorld.X / 16);
		int y = (int)(Main.MouseWorld.Y / 16);
		if (x <= 0 || x >= Main.maxTilesX - 1 || y <= 0 || y >= Main.maxTilesY - 1) return false;
		if (Item.stack <= 0) return false;

		var cell = new Pipelike.ItemPipe.ItemPipeCell(
			MaterialId:   "simple_item",
			Size:         PipeSize.Normal,
			Restrictive:  false,
			Priority:     2,
			// 16 items/s = 0.25 stacks/s -> upstream's "(int)(rate*64+0.5) items/s" branch.
			TransferRate: 0.25f,
			IsSimple:     true);

		if (!Pipelike.ItemPipe.ItemPipeLayerHandle.Instance.TryPlace(cell, x, y, player))
			return false;

		// Auto-INSERT on placement via the same server-authoritative packet
		// the panel uses (cell already exists server-side via TryPlace).
		AutoInsertOnAdjacentStorage(Pipelike.PipeKind.Item, x, y);

		Item.stack--;
		return true;
	}

	internal static void AutoInsertOnAdjacentStorage(Pipelike.PipeKind layer, int x, int y)
	{
		foreach (var side in CoverSides.All)
		{
			var (dx, dy) = side switch
			{
				CoverSide.Up    => (0, -1),
				CoverSide.Down  => (0,  1),
				CoverSide.Left  => (-1, 0),
				CoverSide.Right => ( 1, 0),
				_               => (0, 0),
			};
			var arrival = WorldCapability.ToIODirection(side).Opposite();
			bool hasStorage = layer == Pipelike.PipeKind.Item
				? WorldCapability.ItemHandlerAt (x + dx, y + dy, arrival) != null
				: WorldCapability.FluidHandlerAt(x + dx, y + dy, arrival) != null;
			if (!hasStorage) continue;
			SimplePipeSideSetPacket.Send(layer, x, y, side, SimpleSideMode.Insert);
		}
	}

	void ITextureWarmUp.WarmUpTexture() => ItemIconBaker.Install(Item.type, Texture);

	private int _removeCooldown;

	// Same helper PipeItem uses - hover hint, cell-info, RMB cut.
	public override void HoldItem(Player player)
	{
		((ITextureWarmUp)this).WarmUpTexture();
		PipeHeldItemBehavior.Tick(player, Pipelike.PipeKind.Item, "Simple Item Pipe",
			Pipelike.ItemPipe.ItemPipeLayerHandle.Instance,
			ref _removeCooldown, Item.useTime);
	}
}
