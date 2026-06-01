#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using GregTechCEuTerraria.TerrariaCompat.Pipelike;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.LongDistance;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Pipes;

// Base for the two long-distance pipe items. Dumb LD conduit (no covers, no
// contents). Pair endpoints; insert at input -> emerge at output. Subclasses
// fix Type / id / texture.
public abstract class LongDistancePipeItem : ModItem, ITextureWarmUp
{
	protected abstract LongDistancePipeType PipeType { get; }
	protected abstract LongDistancePipeLayerHandle Handle { get; }
	protected abstract string TransportLine { get; }

	protected override bool CloneNewInstances => true;

	public override void SetDefaults()
	{
		Item.maxStack = 999;
		Item.width = 32; Item.height = 32;
		Item.useTime = 8; Item.useAnimation = 8;
		Item.useStyle = ItemUseStyleID.Swing;
		Item.autoReuse = true;
		Item.consumable = false;
		Item.rare = ItemRarityID.Pink;
		Item.UseSound = SoundID.Item50;
	}

	public override void ModifyTooltips(List<TooltipLine> tooltips)
	{
		base.ModifyTooltips(tooltips);
		tooltips.Add(new TooltipLine(Mod, "PipeKind", "Long Distance Pipeline"));
		tooltips.Add(new TooltipLine(Mod, "PipeUse1",
			$"[c/AAFFAA:{TransportLine}]"));
		tooltips.Add(new TooltipLine(Mod, "PipeUse2",
			"[c/AAAAAA:1. Lay a run of pipe between two distant points.]"));
		tooltips.Add(new TooltipLine(Mod, "PipeUse3",
			"[c/AAAAAA:2. Cap each end with a matching Pipeline Endpoint.]"));
		tooltips.Add(new TooltipLine(Mod, "PipeUse4",
			"[c/AAAAAA:3. Screwdriver an endpoint to set it as Input or Output.]"));
		tooltips.Add(new TooltipLine(Mod, "PipeUse5",
			"[c/888888:Cuts with a wrench or right-click while held. Endpoints must be far apart.]"));
	}

	public override bool? UseItem(Player player)
	{
		if (Main.myPlayer != player.whoAmI) return null;
		int x = (int)(Main.MouseWorld.X / 16);
		int y = (int)(Main.MouseWorld.Y / 16);
		if (x <= 0 || x >= Main.maxTilesX - 1 || y <= 0 || y >= Main.maxTilesY - 1) return false;
		if (Item.stack <= 0) return false;

		if (!Handle.TryPlace(x, y, player)) return false;

		Item.stack--;
		return true;
	}

	void ITextureWarmUp.WarmUpTexture() => ItemIconBaker.Install(Item.type, Texture);

	private int _removeCooldown;

	public override void HoldItem(Player player)
	{
		((ITextureWarmUp)this).WarmUpTexture();
		PipeHeldItemBehavior.Tick(player, PipeKind.LongDistance, "Long Distance Pipeline",
			Handle, ref _removeCooldown, Item.useTime);
	}
}

public sealed class LongDistanceItemPipeItem : LongDistancePipeItem
{
	public override string Name    => "long_distance_item_pipeline";
	public override string Texture => "GregTechCEuTerraria/Content/Textures/block/pipe/ld_item_pipe/block";
	protected override LongDistancePipeType PipeType => LongDistancePipeType.Item;
	protected override LongDistancePipeLayerHandle Handle => LongDistancePipeLayerHandle.Item;
	protected override string TransportLine => "Teleports items between two far-apart endpoints.";

	public override void SetStaticDefaults() =>
		Terraria.Localization.Language.GetOrRegister(
			$"Mods.GregTechCEuTerraria.Items.{Name}.DisplayName",
			() => "Long Distance Item Pipeline");
}

public sealed class LongDistanceFluidPipeItem : LongDistancePipeItem
{
	public override string Name    => "long_distance_fluid_pipeline";
	public override string Texture => "GregTechCEuTerraria/Content/Textures/block/pipe/ld_fluid_pipe/block";
	protected override LongDistancePipeType PipeType => LongDistancePipeType.Fluid;
	protected override LongDistancePipeLayerHandle Handle => LongDistancePipeLayerHandle.Fluid;
	protected override string TransportLine => "Teleports fluids between two far-apart endpoints.";

	public override void SetStaticDefaults() =>
		Terraria.Localization.Language.GetOrRegister(
			$"Mods.GregTechCEuTerraria.Items.{Name}.DisplayName",
			() => "Long Distance Fluid Pipeline");
}
