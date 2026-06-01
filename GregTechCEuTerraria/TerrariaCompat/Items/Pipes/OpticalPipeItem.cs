#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using GregTechCEuTerraria.TerrariaCompat.Pipelike;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Optical;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Pipes;

// Single upstream variant - OpticalPipeType.NORMAL. Carries research data
// and CWU along one axis between transmitter/receiver hatches. Mirror of
// LaserPipeItem.
public sealed class OpticalPipeItem : ModItem, ITextureWarmUp
{
	public override string Name    => "normal_optical_pipe";
	public override string Texture => "GregTechCEuTerraria/Content/Textures/block/pipe/pipe_optical_in";
	protected override bool CloneNewInstances => true;

	public override void SetStaticDefaults()
	{
		Language.GetOrRegister(
			$"Mods.GregTechCEuTerraria.Items.{Name}.DisplayName",
			() => "Optical Pipe");
	}

	public override void SetDefaults()
	{
		Item.maxStack = 999;
		Item.width = 32; Item.height = 32;
		Item.useTime = 8; Item.useAnimation = 8;
		Item.useStyle = ItemUseStyleID.Swing;
		Item.autoReuse = true;
		Item.consumable = false;
		Item.rare = ItemRarityID.Cyan;
		Item.UseSound = SoundID.Item50;
	}

	public override void ModifyTooltips(List<TooltipLine> tooltips)
	{
		base.ModifyTooltips(tooltips);
		tooltips.Add(new TooltipLine(Mod, "PipeKind", "Optical Pipe"));
		tooltips.Add(new TooltipLine(Mod, "PipeData",
			"[c/AAFFFF:Transmits research data + computation (CWU/t) between optical hatches.]"));
		tooltips.Add(new TooltipLine(Mod, "PipeAxis",
			"[c/AAAAAA:Connects only along one axis - no bends or T-junctions.]"));
	}

	public override bool? UseItem(Player player)
	{
		if (Main.myPlayer != player.whoAmI) return null;
		int x = (int)(Main.MouseWorld.X / 16);
		int y = (int)(Main.MouseWorld.Y / 16);
		if (x <= 0 || x >= Main.maxTilesX - 1 || y <= 0 || y >= Main.maxTilesY - 1) return false;
		if (Item.stack <= 0) return false;

		if (!OpticalPipeLayerHandle.Instance.TryPlace(x, y, player)) return false;

		Item.stack--;
		return true;
	}

	void ITextureWarmUp.WarmUpTexture() => ItemIconBaker.Install(Item.type, Texture);

	private int _removeCooldown;

	public override void HoldItem(Player player)
	{
		((ITextureWarmUp)this).WarmUpTexture();
		PipeHeldItemBehavior.Tick(player, PipeKind.Optical, "Optical Pipe",
			OpticalPipeLayerHandle.Instance,
			ref _removeCooldown, Item.useTime);
	}
}
