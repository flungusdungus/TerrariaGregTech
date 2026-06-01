#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Registry;

// Plain inert GT item materialised from items.json - anything whose upstream
// Java class is `net.minecraft.world.item.Item` (circuits, components, wafers,
// boards, boules, processors, molds, hulls, ...). No behaviour; just texture
// + data. Name = bare upstream id, single id-space.
public sealed class RegistryItem : ModItem, ITextureWarmUp
{
	private readonly string? _id;       // bare id, e.g. "silicon_boule"
	private readonly string? _label;
	private readonly int _maxStack;
	private readonly int _rarity;
	private readonly string? _texturePath;  // explicit override; null -> item/<id>

	public RegistryItem() { }
	public RegistryItem(string id, string label, int maxStack, int rarity, string? texturePath = null)
	{
		_id = id;
		_label = label;
		_maxStack = maxStack;
		_rarity = rarity;
		_texturePath = texturePath;
	}

	public override bool IsLoadingEnabled(Mod mod) => _id != null;
	public override string Name => _id ?? nameof(RegistryItem);

	// REQUIRED - see CoverItem for the same trap. Otherwise `_id` lands null
	// and the item saves under "RegistryItem" -> invalid on reload.
	protected override bool CloneNewInstances => true;

	// Optional override for items that reuse another item's texture
	// (e.g. power units). Registry filters ids with no PNG.
	public override string Texture =>
		_texturePath ?? $"GregTechCEuTerraria/Content/Textures/item/{Name}";

	public override void SetStaticDefaults()
	{
		if (_label != null)
			Terraria.Localization.Language.GetOrRegister(
				$"Mods.GregTechCEuTerraria.Items.{Name}.DisplayName", () => _label);

		if (Main.dedServ) return;

		// Vertical frame-strip detection (e.g. Crystal Processor). mcmeta
		// frametime is MC ticks (20 Hz) -> x3 for Terraria's 60 Hz animator.
		var tex = ModContent.Request<Texture2D>(Texture, AssetRequestMode.ImmediateLoad).Value;
		if (tex.Width > 0 && tex.Height > tex.Width && tex.Height % tex.Width == 0)
		{
			int frames = tex.Height / tex.Width;
			var (frameTime, _) = McMeta.Read(Mod, $"Content/Textures/item/{Name}.png.mcmeta");
			Main.RegisterItemAnimation(Type, new DrawAnimationVertical(frameTime * 3, frames));
		}
	}

	public override void SetDefaults()
	{
		Item.maxStack = 999;
		Item.width = 32;
		Item.height = 32;
		Item.value = Terraria.Item.buyPrice(silver: 2);
		Item.rare = _rarity;
	}

	public override void HoldItem(Player player)
	{
		base.HoldItem(player);
		ItemIconBaker.Install(Item.type, Texture);
	}

	void ITextureWarmUp.WarmUpTexture() => ItemIconBaker.Install(Item.type, Texture);
}
