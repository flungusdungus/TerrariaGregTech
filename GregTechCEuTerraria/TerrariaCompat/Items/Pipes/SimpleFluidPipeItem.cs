#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using GregTechCEuTerraria.TerrariaCompat.Pipelike;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Pipes;

// Fluid twin of SimpleItemPipeItem. Sentinel MaterialId "simple_fluid".
// Stats matched to a wooden fluid pipe - 20 mB/t, single channel, no
// gas/cryo/plasma/acid containment.
public sealed class SimpleFluidPipeItem : ModItem, ITextureWarmUp
{
	public override string Name     => "simple_fluid_pipe";
	public override string Texture  => "GregTechCEuTerraria/Content/Textures/block/pipe/pipe_normal_in";
	protected override bool CloneNewInstances => true;

	public override void SetStaticDefaults()
	{
		Language.GetOrRegister(
			$"Mods.GregTechCEuTerraria.Items.{Name}.DisplayName",
			() => "Simple Fluid Pipe");
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
		tooltips.Add(new TooltipLine(Mod, "PipeKind", "Simple Fluid Pipe"));
		tooltips.Add(new TooltipLine(Mod, "PipeRate", "[c/55FFFF:Transfer Rate:] 20 mB/t"));
		tooltips.Add(new TooltipLine(Mod, "PipeTemp", "[c/FF5555:Temperature Limit:] 300 K"));
		tooltips.Add(new TooltipLine(Mod, "PipeNotGasProof", "[c/AA0000:Gases may leak!]"));
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

		var cell = new Pipelike.Fluid.FluidPipeCell(
			MaterialId:          "simple_fluid",
			Size:                PipeSize.Normal,
			Throughput:          20,
			Channels:            1,
			MaxFluidTemperature: 300,
			GasProof:            false,
			CryoProof:           false,
			PlasmaProof:         false,
			AcidProof:           false,
			IsSimple:            true);

		if (!Pipelike.Fluid.FluidPipeLayerHandle.Instance.TryPlace(cell, x, y, player))
			return false;

		SimpleItemPipeItem.AutoInsertOnAdjacentStorage(Pipelike.PipeKind.Fluid, x, y);

		Item.stack--;
		return true;
	}

	void ITextureWarmUp.WarmUpTexture() => ItemIconBaker.Install(Item.type, Texture);

	private int _removeCooldown;

	public override void HoldItem(Player player)
	{
		((ITextureWarmUp)this).WarmUpTexture();
		PipeHeldItemBehavior.Tick(player, Pipelike.PipeKind.Fluid, "Simple Fluid Pipe",
			Pipelike.Fluid.FluidPipeLayerHandle.Instance,
			ref _removeCooldown, Item.useTime);
	}
}
