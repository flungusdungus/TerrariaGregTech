#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.BossDrops.MultiblockBag;

// One-bag-per-multi treasure container. Right-click -> spawns the structure
// parts needed to build the multi at its max size. Contents resolved once at
// Mod.Load (MultiblockBagContents.Resolve) and cached on the prototype.
//
// Sentinel pattern (same as RegistryItem / CoverItem): the parameterless
// ctor is the autoload probe; IsLoadingEnabled is false for it so tML's
// autoload sweep doesn't double-register. MultiblockBagLoader registers one
// real instance per multi.
public sealed class MultiblockBagItem : ModItem, ITextureWarmUp
{
	private readonly string? _multiId;
	private readonly string? _label;
	private List<MultiblockBagContents.Drop>? _contents;

	public MultiblockBagItem() { }
	public MultiblockBagItem(string multiId, string label)
	{
		_multiId = multiId;
		_label = label;
	}

	public string MultiId => _multiId ?? "";

	public override bool IsLoadingEnabled(Mod mod) => _multiId != null;
	public override string Name => _multiId != null ? $"{MultiblockBagLoader.NamePrefix}{_multiId}" : nameof(MultiblockBagItem);

	// Per-instance fields require CloneNewInstances so per-stack cloned items
	// still carry the multi id (same reasoning as CoverItem / MaterialItem).
	protected override bool CloneNewInstances => true;

	// Composited base + overlay icon installed by MultiblockBagArt.WarmUp.
	// Placeholder until then.
	public override string Texture => "GregTechCEuTerraria/Content/TerrariaCompat/TooManyItemsItem";

	public void WarmUpTexture()
	{
		if (_multiId is null) return;
		MultiblockBagArt.InstallFor(Item.type, _multiId);
	}

	public override void SetStaticDefaults()
	{
		if (_label != null)
		{
			Language.GetOrRegister(
				$"Mods.GregTechCEuTerraria.Items.{Name}.DisplayName",
				() => $"{_label} Multiblock Bag");
			Language.GetOrRegister(
				$"Mods.GregTechCEuTerraria.Items.{Name}.Tooltip",
				() => "Right-click to open - drops the structure parts to assemble this multiblock.");
		}
	}

	public override void SetDefaults()
	{
		Item.width = 34;        // matches vanilla treasure bags
		Item.height = 34;
		Item.maxStack = 999;
		Item.rare = ItemRarityID.Cyan;
		Item.consumable = true;
		Item.useStyle = ItemUseStyleID.HoldUp;
		Item.useTime = 20;
		Item.useAnimation = 20;
	}

	public override bool CanRightClick() => true;

	public override void RightClick(Player player)
	{
		EnsureContentsResolved();
		if (_contents is null) return;
		var src = new EntitySource_Gift(player, $"GregTechCEuTerraria/MultiblockBag/{_multiId}");
		// PlayerGive: drop straight into the local player's inventory on the
		// owning client (no world-item entity + grab-tick latency); world-drop
		// only as overflow when inventory is full.
		foreach (var d in _contents)
			global::GregTechCEuTerraria.TerrariaCompat.Utils.PlayerGive.Give(player, src, d.ItemType, d.Count);
	}

	private void EnsureContentsResolved()
	{
		if (_contents != null || _multiId == null) return;
		if (!MachineRegistry.TryGet(_multiId, out var def)) return;
		_contents = MultiblockBagContents.Resolve(Mod, def);
	}

	public override void ModifyTooltips(List<TooltipLine> tooltips)
	{
		EnsureContentsResolved();
		if (_contents is null || _contents.Count == 0) return;
		tooltips.Add(new TooltipLine(Mod, "BagHeader", "[c/AAEEFF:Contents]"));
		foreach (var d in _contents)
			tooltips.Add(new TooltipLine(Mod, $"BagItem_{d.ItemType}",
				$"  {d.Count}x {Lang.GetItemName(d.ItemType).Value}"));
	}
}
