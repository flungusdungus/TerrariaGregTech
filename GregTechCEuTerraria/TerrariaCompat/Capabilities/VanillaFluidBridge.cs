#nullable enable
using GregTechCEuTerraria.Api.Data.Chemical.Material;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Fluids.Store;
using GregTechCEuTerraria.Api.Fluids;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Capabilities;

// Bridge between vanilla Terraria bucket items and our FluidStack model.
// One MC bucket == 1000 mB (mirrors Minecraft convention since upstream uses it).
//
// Only knows about vanilla buckets - modded bucket-like items would register
// their own bridge entries (future work). Lava/honey added as obvious next
// steps once we have those fluid types defined.
public static class VanillaFluidBridge
{
	public const int BucketAmount = 1000;

	// Returns the fluid a vanilla bucket item represents, or Empty if the
	// item isn't a known bucket.
	public static FluidStack StackFor(int itemType) => itemType switch
	{
		ItemID.WaterBucket => new FluidStack(FluidRegistry.Water, BucketAmount),
		ItemID.LavaBucket  => new FluidStack(FluidRegistry.Lava,  BucketAmount),
		// TODO: HoneyBucket, BottomlessBucket variants
		_ => FluidStack.Empty,
	};

	// Returns the ItemID of the empty-bucket variant of a filled bucket, or
	// 0 if not a known bucket.
	public static int EmptyVersion(int filledType) => filledType switch
	{
		ItemID.WaterBucket => ItemID.EmptyBucket,
		ItemID.LavaBucket  => ItemID.EmptyBucket,
		_ => 0,
	};

	// Inverse: given an empty bucket type + fluid type, returns the filled
	// bucket ItemID, or 0 if unsupported.
	public static int FilledVersion(int emptyType, FluidType fluid)
	{
		if (emptyType != ItemID.EmptyBucket) return 0;
		if (fluid.Id == FluidRegistry.Water.Id) return ItemID.WaterBucket;
		if (fluid.Id == FluidRegistry.Lava.Id)  return ItemID.LavaBucket;
		return 0;
	}
}
