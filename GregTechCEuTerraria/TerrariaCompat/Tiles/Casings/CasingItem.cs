#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Casings;

// Placeable item for a casing block - one per CasingTile, sharing the upstream
// id as its Name (single id-space: locale key, recipe-resolver key and the
// sibling CasingTile all key off this string). Dump-driven by CasingRegistry.
public sealed class CasingItem : ModItem, ITextureWarmUp
{
	private readonly string? _id;
	private readonly string? _texture;       // gtceu-relative block texture path
	private readonly string? _displayName;
	private readonly int _maxStack;
	private readonly int _rarity;

	public CasingItem() { }
	public CasingItem(string id, string texture, string displayName, int maxStack, int rarity)
	{
		_id = id;
		_texture = texture;
		_displayName = displayName;
		_maxStack = maxStack;
		_rarity = rarity;
	}

	public override bool IsLoadingEnabled(Mod mod) => _id != null;
	public override string Name => _id ?? nameof(CasingItem);
	// Placeholder asset - WarmUpTexture overrides TextureAssets.Item. See TooManyItemsArt.
	public override string Texture => "GregTechCEuTerraria/Content/TerrariaCompat/TooManyItemsItem";
	protected override bool CloneNewInstances => true;

	public override void SetStaticDefaults()
	{
		// Self-register the upstream DisplayName (the casing isn't in the
		// generated en-US.hjson) - mirrors how machines name themselves.
		if (_id != null && !string.IsNullOrEmpty(_displayName))
			Language.GetOrRegister(
				$"Mods.GregTechCEuTerraria.Items.{Name}.DisplayName", () => _displayName!);
	}

	public override void SetDefaults()
	{
		if (_id is null) return;
		Item.DefaultToPlaceableTile(Mod.Find<ModTile>(Name).Type);
		Item.width = 32;
		Item.height = 32;
		Item.maxStack = 999;
		Item.rare = _rarity;
	}

	public void WarmUpTexture()
	{
		if (_texture is not null)
			CasingRenderer.EnsureItemTexture(Type, _texture);
	}
}
