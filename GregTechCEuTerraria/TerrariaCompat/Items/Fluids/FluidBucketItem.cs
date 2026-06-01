#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Fluids;

// One bucket item per registered FluidType with HasBucket - creative/testing
// fluid source. RMB on a machine fluid slot fills (UIFluidSlot); not consumed.
public sealed class FluidBucketItem : ModItem, ITextureWarmUp, IFluidHandlerItem
{
	private const int BucketCapacity = 1000;

	[CloneByReference] private readonly FluidType? _fluid;

	public FluidBucketItem() { }
	public FluidBucketItem(FluidType fluid) { _fluid = fluid; }

	public FluidType? Fluid => _fluid;

	// Read-only IFluidHandlerItem view so any walker (e.g. recipe browser's
	// "Have ingredients" filter) treats buckets and cells uniformly. Fill/Drain
	// are no-ops - buckets transfer via UIFluidSlot's bucket-specific path;
	// auto-transfer / pipes must NOT pull from them.
	public Item Container => Item;
	public int TankCount => 1;
	public FluidStack GetTank(int tank) =>
		_fluid is null ? default : new FluidStack(_fluid, BucketCapacity);
	public int GetCapacity(int tank) => BucketCapacity;
	public int Fill(FluidStack fluid, bool simulate) => 0;
	public FluidStack Drain(int maxAmount, bool simulate) => default;
	public FluidStack Drain(FluidStack fluidStack, bool simulate) => default;

	public override string Name => _fluid != null ? $"{_fluid.Id}_bucket" : nameof(FluidBucketItem);
	// Placeholder; PreDraw installs the procedural icon via FluidBucketRenderer.
	public override string Texture => "GregTechCEuTerraria/Content/TerrariaCompat/TooManyItemsItem";
	public override bool IsLoadingEnabled(Mod mod) => _fluid != null;
	protected override bool CloneNewInstances => true;

	public override void SetStaticDefaults()
	{
		if (_fluid is null) return;
		Language.GetOrRegister($"Mods.GregTechCEuTerraria.Items.{Name}.DisplayName",
			() => $"{_fluid.DisplayName} Bucket");
	}

	public override void SetDefaults()
	{
		// Match vanilla Empty Bucket physics - icon is composited from it.
		Item.CloneDefaults(ItemID.EmptyBucket);
		Item.maxStack = Item.CommonMaxStack;
		Item.value = 0;
		Item.rare = ItemRarityID.Blue;
	}

	public void WarmUpTexture()
	{
		if (_fluid != null)
			FluidBucketRenderer.EnsureItemTexture(Item.type, _fluid);
	}

	public override bool PreDrawInInventory(SpriteBatch sb, Vector2 position, Rectangle frame,
		Color drawColor, Color itemColor, Vector2 origin, float scale)
	{
		WarmUpTexture();
		return true;
	}

	public override bool PreDrawInWorld(SpriteBatch sb, Color lightColor, Color alphaColor,
		ref float rotation, ref float scale, int whoAmI)
	{
		WarmUpTexture();
		return true;
	}

	public override void ModifyTooltips(List<TooltipLine> tooltips)
	{
		if (_fluid is null) return;
		tooltips.Add(new TooltipLine(Mod, "FluidContents", $"Contains 1000 mB {_fluid.DisplayName}"));
		foreach (var attr in _fluid.Attributes)
			attr.AppendFluidTooltips(s => tooltips.Add(new TooltipLine(Mod, "FluidAttribute", s)));
	}
}
