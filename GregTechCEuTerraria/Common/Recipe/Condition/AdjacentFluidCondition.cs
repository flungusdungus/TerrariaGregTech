#nullable enable
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;
using Terraria.ID;

namespace GregTechCEuTerraria.Common.Recipe.Condition;

// LOCKED - Terraria-adapted port of
// com.gregtechceu.gtceu.common.recipe.condition.AdjacentFluidCondition.
//
// Recipe requires a specific fluid to be adjacent to the machine. Used by
// machines that draw fluid from world-placed liquid (e.g. lava intake for
// a steam boiler).
//
// Documented adaptation:
//   - Terraria has 3 built-in liquids (water/lava/honey/shimmer) addressed
//     via Main.tile[x,y].LiquidType (LiquidID.Water/Lava/Honey/Shimmer).
//   - Upstream uses fluid IDs (minecraft:water, minecraft:lava). The JSON
//     loader maps these to LiquidID.* at parse time; our condition stores
//     the resolved LiquidID byte.
public sealed class AdjacentFluidCondition : RecipeCondition
{
	public short RequiredLiquidType { get; }   // LiquidID.Water / .Lava / .Honey / .Shimmer
	public int MinCount { get; }

	public AdjacentFluidCondition() : this((short)LiquidID.Water, 1) { }
	public AdjacentFluidCondition(short liquidType, int minCount) { RequiredLiquidType = liquidType; MinCount = minCount; }

	public override bool Test(RecipeLogic logic)
	{
		if (logic.GetRLMachine() is not MetaMachine mte) return true;
		int count = 0;
		var pos = mte.Position;
		var (w, h) = mte.Size;
		for (int dx = -1; dx <= w; dx++)
		{
			Check(pos.X + dx, pos.Y - 1, ref count);
			Check(pos.X + dx, pos.Y + h, ref count);
		}
		for (int dy = 0; dy < h; dy++)
		{
			Check(pos.X - 1, pos.Y + dy, ref count);
			Check(pos.X + w, pos.Y + dy, ref count);
		}
		return count >= MinCount;
	}

	private void Check(int x, int y, ref int count)
	{
		if (x < 0 || x >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY) return;
		var tile = Main.tile[x, y];
		if (tile.LiquidAmount > 0 && tile.LiquidType == RequiredLiquidType) count++;
	}

	public override string GetTooltips() =>
		$"Requires {MinCount}x adjacent liquid id #{RequiredLiquidType}";
	public override string GetTypeName() => "gtceu:adjacent_fluid";
}
