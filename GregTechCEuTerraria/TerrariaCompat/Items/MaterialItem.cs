#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Common.Materials;
using GregTechCEuTerraria.Api.Data.Chemical.Material;
using GregTechCEuTerraria.TerrariaCompat.Items.Registry;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items;

// One ModItem per material x prefix item via MaterialItemRegistry. Rendering
// replays the dump's RenderLayers verbatim (upstream texture path + exact
// `Material.getLayerARGB` tint) - no iconset guessing, no tint deviation.
// `argb == -1` -> opaque white = untinted.
public sealed class MaterialItem : ModItem, ITextureWarmUp
{
	private readonly string? _id;
	[CloneByReference] private readonly Material? _material;
	[CloneByReference] private readonly MaterialPrefix? _prefix;
	[CloneByReference] private readonly IReadOnlyList<RegistryDump.RenderLayer> _layers;

	private const int AnimFrameTicks = 5;
	private const string TextureRoot = "GregTechCEuTerraria/Content/Textures/";

	public MaterialItem()
	{
		_layers = Array.Empty<RegistryDump.RenderLayer>();
	}

	public MaterialItem(string id, Material material, MaterialPrefix prefix,
		IReadOnlyList<RegistryDump.RenderLayer> layers)
	{
		_id = id;
		_material = material;
		_prefix = prefix;
		_layers = layers;
	}

	public override string Name => _id ?? nameof(MaterialItem);
	protected override bool CloneNewInstances => true;
	public override bool IsLoadingEnabled(Mod mod) => _id != null;

	// Primary tex = layer 0; PreDraw composites the rest.
	public override string Texture => _layers.Count > 0
		? TextureRoot + _layers[0].Texture
		: "Terraria/Images/Item_22";

	public override void SetStaticDefaults()
	{
		base.SetStaticDefaults();
		if (Main.dedServ) return;
		// Detect upstream vertical-strip animation on layer 0 (e.g. gem grades
		// ship an 8-frame column). Force-load via the known path -
		// TextureAssets.Item[type] isn't populated yet.
		if (_layers.Count == 0) return;
		string path = TextureRoot + _layers[0].Texture;
		if (!ModContent.HasAsset(path)) return;
		var tex = ModContent.Request<Texture2D>(path, AssetRequestMode.ImmediateLoad).Value;
		if (tex.Width > 0 && tex.Height > tex.Width && tex.Height % tex.Width == 0)
		{
			int frameCount = tex.Height / tex.Width;
			Main.RegisterItemAnimation(Item.type,
				new Terraria.DataStructures.DrawAnimationVertical(AnimFrameTicks, frameCount));
		}
	}

	public override void SetDefaults()
	{
		Item.maxStack = 999;
		Item.width = 32;
		Item.height = 32;
		Item.value = 0;
		Item.rare = ItemRarityID.White;
		if (_prefix?.Id is "block" or "raw_ore_block" or "frame" && _id is not null)
		{
			int? tileType = Tiles.MaterialBlockTileRegistry.Get(_id);
			if (tileType.HasValue) Item.DefaultToPlaceableTile(tileType.Value);
		}
	}

	// Upstream TooltipsHandler.appendTooltips: chemical formula as a yellow
	// literal inserted at index 1 (just below the item name).
	public override void ModifyTooltips(List<TooltipLine> tooltips)
	{
		base.ModifyTooltips(tooltips);
		string? formula = _material?.Formula;
		if (string.IsNullOrEmpty(formula)) return;
		var line = new TooltipLine(Mod, "ChemicalFormula", formula)
		{
			OverrideColor = new Color(255, 255, 85), // ChatFormatting.YELLOW
		};
		int nameIdx = tooltips.FindIndex(t => t.Mod == "Terraria" && t.Name == "ItemName");
		if (nameIdx >= 0) tooltips.Insert(nameIdx + 1, line);
		else tooltips.Add(line);
	}

	public override void HoldItem(Player player)
	{
		base.HoldItem(player);
		EnsureTextureBaked();
	}

	void ITextureWarmUp.WarmUpTexture() => EnsureTextureBaked();

	private void EnsureTextureBaked()
	{
		if (_layers.Count == 0) return;
		var iconLayers = new IconLayer[_layers.Count];
		for (int i = 0; i < _layers.Count; i++)
			iconLayers[i] = new IconLayer(TextureRoot + _layers[i].Texture, Tint(_layers[i].Argb));
		ItemIconBaker.Install(Item.type, iconLayers);
	}

	// Upstream getLayerARGB (0xAARRGGBB; -1 = untinted opaque-white passthrough).
	private static Color Tint(int argb) => new(
		(byte)((argb >> 16) & 0xFF),
		(byte)((argb >> 8) & 0xFF),
		(byte)(argb & 0xFF),
		(byte)((argb >> 24) & 0xFF));
}
