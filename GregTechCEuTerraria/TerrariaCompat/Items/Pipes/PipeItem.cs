#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Data.Chemical.Material;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using GregTechCEuTerraria.TerrariaCompat.Pipelike;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Pipes;

// Material-keyed placeable pipe - counterpart of WireItem. One instance per
// (Material x pipe prefix) via PipeItemRegistry from MaterialPipeBlockItem dump.
public sealed class PipeItem : ModItem, ITextureWarmUp
{
	private readonly string? _id;          // exact upstream registry id
	private readonly string? _label;
	[CloneByReference] private readonly Material? _material;
	private readonly string _sizeWord = "normal"; // tiny/small/normal/large/huge/quadruple/nonuple
	private readonly PipeKind _kind = PipeKind.Item;
	// Item-pipe only - last-resort routing priority; rides through to ItemPipeCell.
	private readonly bool _restrictive;

	// Read by layer systems' PostDrawTiles hooks to gate the foreground overlay.
	public PipeKind Kind => _kind;

	public PipeItem() { }
	public PipeItem(string id, string label, Material material, string sizeWord, PipeKind kind, bool restrictive = false)
	{
		_id = id;
		_label = label;
		_material = material;
		_sizeWord = sizeWord;
		_kind = kind;
		_restrictive = restrictive;
	}

	public override string Name => _id ?? nameof(PipeItem);

	// Raw upstream pipe-end (16x16, untinted); ItemIconBaker installs the
	// material-tinted 2x upscale into TextureAssets.Item at runtime.
	public override string Texture =>
		$"GregTechCEuTerraria/Content/Textures/block/pipe/pipe_{_sizeWord}_in";
	protected override bool CloneNewInstances => true;
	public override bool IsLoadingEnabled(Mod mod) => _material != null;

	public override void SetStaticDefaults()
	{
		if (_label != null)
			Language.GetOrRegister($"Mods.GregTechCEuTerraria.Items.{Name}.DisplayName", () => _label);
	}

	private int _removeCooldown;

	public override void SetDefaults()
	{
		Item.maxStack = 999;
		Item.width = 32;
		Item.height = 32;
		Item.useTime = 8;
		Item.useAnimation = 8;
		Item.useStyle = ItemUseStyleID.Swing;
		Item.autoReuse = true;
		Item.consumable = false; // manual stack management
		Item.rare = ItemRarityID.White;
		Item.UseSound = SoundID.Item50;
	}

	public override void ModifyTooltips(List<TooltipLine> tooltips)
	{
		base.ModifyTooltips(tooltips);
		if (_material is null) return;
		tooltips.Add(new TooltipLine(Mod, "PipeKind",
			$"{Capitalize(_sizeWord)} {KindWord(_kind, _restrictive)}"));

		// Format mirrors upstream FluidPipeBlock / ItemPipeBlock.appendHoverText.
		if (_kind == Pipelike.PipeKind.Fluid)
		{
			var c = BuildFluidCell();
			if (c is null) return;
			var f = c.Value;
			tooltips.Add(new TooltipLine(Mod, "PipeThroughput",
				$"[c/55FFFF:Transfer Rate:] {f.Throughput:N0} mB/t"));
			tooltips.Add(new TooltipLine(Mod, "PipeMaxTemp",
				$"[c/FF5555:Temperature Limit:] {f.MaxFluidTemperature} K"));
			if (f.Channels > 1)
				tooltips.Add(new TooltipLine(Mod, "PipeChannels",
					$"[c/FFFF55:Channels:] {f.Channels}"));
			tooltips.Add(f.GasProof
				? new TooltipLine(Mod, "PipeGasProof",   "[c/FFAA00:Can handle Gases]")
				: new TooltipLine(Mod, "PipeNotGasProof","[c/AA0000:Gases may leak!]"));
			if (f.AcidProof)    tooltips.Add(new TooltipLine(Mod, "PipeAcidProof",   "[c/FFAA00:Can handle Acids]"));
			if (f.CryoProof)    tooltips.Add(new TooltipLine(Mod, "PipeCryoProof",   "[c/FFAA00:Can handle Cryogenics]"));
			if (f.PlasmaProof)  tooltips.Add(new TooltipLine(Mod, "PipePlasmaProof", "[c/FFAA00:Can handle all Plasmas]"));
		}
		else
		{
			// Verbatim ItemPipeBlock.appendHoverText: int -> "N stacks/s",
			// fractional -> "(Nx64) items/s".
			var i = BuildItemCell();
			float rate = i.TransferRate;
			string rateLine = (rate % 1 != 0f)
				? $"[c/55FFFF:Transfer Rate:] {(int)((rate * 64) + 0.5f)} items/s"
				: $"[c/55FFFF:Transfer Rate:] {(int)rate} stacks/s";
			tooltips.Add(new TooltipLine(Mod, "PipeTransferRate", rateLine));
		}
	}

	// Has/CutAt share signature across all three layers; placement doesn't
	// (per-cell args differ), so UseItem still branches on _kind once.
	private Api.Pipenet.IGridLayerHandle Layer => _kind == Pipelike.PipeKind.Fluid
		? Pipelike.Fluid.FluidPipeLayerHandle.Instance
		: (Api.Pipenet.IGridLayerHandle)Pipelike.ItemPipe.ItemPipeLayerHandle.Instance;

	public override bool? UseItem(Player player)
	{
		if (_material is null) return null;
		if (Main.myPlayer != player.whoAmI) return null;

		int x = (int)(Main.MouseWorld.X / 16);
		int y = (int)(Main.MouseWorld.Y / 16);
		if (x <= 0 || x >= Main.maxTilesX - 1 || y <= 0 || y >= Main.maxTilesY - 1)
			return false;
		if (Item.stack <= 0) return false;

		bool placed;
		if (_kind == Pipelike.PipeKind.Fluid)
		{
			var cell = BuildFluidCell();
			if (cell is null) return false; // material can't form a fluid pipe
			placed = Pipelike.Fluid.FluidPipeLayerHandle.Instance.TryPlace(cell.Value, x, y, player);
		}
		else
		{
			placed = Pipelike.ItemPipe.ItemPipeLayerHandle.Instance.TryPlace(BuildItemCell(), x, y, player);
		}
		if (!placed) return false;
		Item.stack--;
		return true;
	}

	// Single source of truth for material -> cell. Used only by UseItem.
	private Pipelike.ItemPipe.ItemPipeCell BuildItemCell()
	{
		var size = Pipelike.PipeSizes.FromWord(_sizeWord);
		// Falls back to upstream's default `new ItemPipeProperties()` (1, 0.25)
		// when materials.json doesn't carry the data (Material.ItemPipe TODO);
		// per-size resistance multiplier still differentiates the routes.
		var basePriority = _material!.ItemPipe?.Priority     ?? 1;
		var baseRate     = _material .ItemPipe?.TransferRate ?? 0.25f;
		// Verbatim ItemPipeType.modifyProperties.
		var mod = Pipelike.ItemPipe.ItemPipeSizeModifier.For(size, _restrictive);
		int  priority = (int)((basePriority * mod.ResistanceMultiplier) + 0.5f);
		float rate    = baseRate * mod.RateMultiplier;
		return new Pipelike.ItemPipe.ItemPipeCell(
			MaterialId:   _material.Id,
			Size:         size,
			Restrictive:  _restrictive,
			Priority:     priority,
			TransferRate: rate);
	}

	// Null when material has no FluidPipe (safety belt - registry filters first).
	private Pipelike.Fluid.FluidPipeCell? BuildFluidCell()
	{
		var props = _material!.FluidPipe;
		if (props is null) return null;
		// Verbatim FluidPipeType.modifyProperties - without this every size sat
		// at the bare material throughput (200 mB/t for naquadah, all sizes).
		var size = Pipelike.PipeSizes.FromWord(_sizeWord);
		int throughput = props.Throughput * Pipelike.PipeSizes.FluidPipeCapacityMultiplier(size);
		int channels   = Pipelike.PipeSizes.FluidPipeChannels(size);
		return new Pipelike.Fluid.FluidPipeCell(
			MaterialId:          _material.Id,
			Size:                size,
			Throughput:          throughput,
			Channels:            channels,
			MaxFluidTemperature: props.MaxFluidTemperature,
			GasProof:    props.GasProof,
			CryoProof:   props.CryoProof,
			PlasmaProof: props.PlasmaProof,
			AcidProof:   props.AcidProof);
	}

	public override void HoldItem(Player player)
	{
		// Bake-on-hold covers RMB-picked-into-hotbar items not yet drawn in a
		// slot (otherwise the swing reads the raw 16x16 -> magenta placeholder).
		EnsureTextureBaked();
		if (Main.myPlayer != player.whoAmI) return;
		if (_material is null) return;
		// Hover tooltip + RMB-held cut shared with simple pipes via
		// PipeHeldItemBehavior so both behave identically.
		string heldKindLabel = _kind == Pipelike.PipeKind.Fluid
			? "Fluid Pipe"
			: (_restrictive ? "Restrictive Item Pipe" : "Item Pipe");
		PipeHeldItemBehavior.Tick(player, _kind, heldKindLabel, Layer,
			ref _removeCooldown, Item.useTime);
	}

	void ITextureWarmUp.WarmUpTexture() => EnsureTextureBaked();

	private void EnsureTextureBaked() =>
		ItemIconBaker.Install(Item.type,
			$"GregTechCEuTerraria/Content/Textures/block/pipe/pipe_{_sizeWord}_in",
			Tint());

	private static string KindWord(PipeKind kind, bool restrictive) => kind switch
	{
		PipeKind.Fluid => "Fluid Pipe",
		PipeKind.Item  => restrictive ? "Restrictive Item Pipe" : "Item Pipe",
		_              => "Pipe",
	};

	// Pipe-end silhouette is white; multiply per-pixel RGB by material colour.
	private Color Tint()
	{
		uint c = _material?.Color ?? 0xFFFFFFu;
		return new Color((byte)((c >> 16) & 0xFF), (byte)((c >> 8) & 0xFF), (byte)(c & 0xFF));
	}

	private static string Capitalize(string s) =>
		string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
