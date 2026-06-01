#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Covers;

// A cover item - placed into a machine's cover slot. Materialised from the
// registry dump by CoverItemLoader (ComponentItem entries carrying a
// CoverPlaceBehavior). CoverId is the bare cover-definition id this item places.
public sealed class CoverItem : ModItem, ITextureWarmUp
{
	private readonly string? _id;
	private readonly string? _label;
	private readonly int _maxStack;
	private readonly int _rarity;
	private readonly string? _coverId;

	public CoverItem() { }
	public CoverItem(string id, string label, int maxStack, int rarity, string coverId)
	{
		_id = id;
		_label = label;
		_maxStack = maxStack;
		_rarity = rarity;
		_coverId = coverId;
	}

	public string CoverId => _coverId ?? "";

	public override bool IsLoadingEnabled(Mod mod) => _id != null;
	public override string Name => _id ?? nameof(CoverItem);
	public override string Texture => $"GregTechCEuTerraria/Content/Textures/item/{Name}";

	// REQUIRED: without this tML builds each per-Item via the parameterless
	// ctor, leaving `_id` null -> Name degrades to "CoverItem" -> invalid item
	// on world reload. Same pattern as MaterialItem / WireItem / BatteryItem.
	protected override bool CloneNewInstances => true;

	public override void SetStaticDefaults()
	{
		if (_label != null)
		{
			// Force "... Cover" suffix so a search for "cover" surfaces all of
			// them (upstream names are inconsistent).
			string display = _label.Contains("cover", System.StringComparison.OrdinalIgnoreCase)
				? _label
				: _label + " Cover";
			Terraria.Localization.Language.GetOrRegister(
				$"Mods.GregTechCEuTerraria.Items.{Name}.DisplayName", () => display);
		}

		if (Main.dedServ) return;

		// Vertical frame strip -> vertical DrawAnimation. mcmeta frametime is in
		// MC ticks (20 Hz); scale x3 for Terraria's 60 Hz item animator.
		var tex = ModContent.Request<Texture2D>(Texture, AssetRequestMode.ImmediateLoad).Value;
		if (tex.Width > 0 && tex.Height > tex.Width && tex.Height % tex.Width == 0)
		{
			int frames = tex.Height / tex.Width;
			var (frameTime, _) = Machine.Rendering.McMeta.Read(Mod, $"Content/Textures/item/{Name}.png.mcmeta");
			Main.RegisterItemAnimation(Type, new DrawAnimationVertical(frameTime * 3, frames));
		}
	}

	public override void SetDefaults()
	{
		Item.maxStack = 99;
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
