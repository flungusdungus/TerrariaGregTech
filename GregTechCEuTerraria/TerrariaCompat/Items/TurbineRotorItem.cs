#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Data.Chemical.Material;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items;

// Per-material turbine rotor. One Terraria ItemID per material with a rotor
// property (`<material>_turbine_rotor`).
//
// DEVIATION from upstream item identity: upstream is ONE
// `gtceu:turbine_rotor` ComponentItem with material in NBT
// (GT.PartStats.Material). We register per-material; the `nbt:{Material:X}` ->
// X-specific ItemID mapping happens at recipe-parse time via
// NBTPredicateIngredient.ResolveItemTypeFromNbt.
//
// Durability + Damage melee stat dropped (Terraria items don't wear out, rotor
// isn't a weapon in this port).
public sealed class TurbineRotorItem : ModItem, ITextureWarmUp
{
	[CloneByReference] private readonly Material? _material;

	public Material? Material => _material;
	public int Efficiency => _material?.Rotor?.Efficiency ?? -1;
	public int Power      => _material?.Rotor?.Power      ?? -1;

	public TurbineRotorItem() { }
	public TurbineRotorItem(Material material) { _material = material; }

	public override bool IsLoadingEnabled(Mod mod) => _material != null;

	// REQUIRED - see CoverItem / RegistryItem for the same trap.
	protected override bool CloneNewInstances => true;

	public override string Name => _material is null
		? nameof(TurbineRotorItem)
		: $"{_material.Id}_turbine_rotor";

	public override string Texture => "GregTechCEuTerraria/Content/Textures/item/tools/turbine";

	public override void SetStaticDefaults()
	{
		base.SetStaticDefaults();
		if (_material is null) return;
		string matName = Language.GetTextValue($"Mods.GregTechCEuTerraria.Materials.{_material.Id}");
		Language.GetOrRegister($"Mods.GregTechCEuTerraria.Items.{Name}.DisplayName",
			() => $"{matName} Turbine Rotor");
	}

	public override void SetDefaults()
	{
		Item.maxStack   = 1;
		Item.width      = 32;
		Item.height     = 32;
		Item.rare       = ItemRarityID.White;
		Item.consumable = false;
	}

	public override void ModifyTooltips(List<TooltipLine> tooltips)
	{
		base.ModifyTooltips(tooltips);
		if (_material?.Rotor is not { } r) return;
		tooltips.Add(new TooltipLine(Mod, "RotorEfficiency",
			$"Efficiency: {r.Efficiency}%"));
		tooltips.Add(new TooltipLine(Mod, "RotorPower",
			$"Power: {r.Power}"));
	}

	public override void HoldItem(Player player)
	{
		base.HoldItem(player);
		EnsureTextureBaked();
	}

	void ITextureWarmUp.WarmUpTexture() => EnsureTextureBaked();

	private void EnsureTextureBaked()
	{
		uint argb = _material?.Color ?? 0xFFFFFFFFu;
		var tint = new Color(
			(byte)((argb >> 16) & 0xFF),
			(byte)((argb >> 8) & 0xFF),
			(byte)(argb & 0xFF));
		ItemIconBaker.Install(Item.type, Texture, tint);
	}
}
