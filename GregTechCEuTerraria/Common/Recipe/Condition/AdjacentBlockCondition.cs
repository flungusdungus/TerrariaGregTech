#nullable enable
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;

namespace GregTechCEuTerraria.Common.Recipe.Condition;

// LOCKED - Terraria-adapted port of
// com.gregtechceu.gtceu.common.recipe.condition.AdjacentBlockCondition.
//
// Recipe requires a specific block type to be adjacent to the machine.
// Used by recipes that need a structure (e.g. lava nearby for melting).
//
// Adaptation: upstream uses Block identity (Resource Location). Our
// equivalent is the tile-type id (ushort).  Tile IDs come from `TileID.*`
// for vanilla / `ModContent.TileType<T>()` for mod tiles. The upstream
// blockId string is resolved at JSON read time via an IIngredientResolver-
// style bridge (or the JSON loader hardcodes the mapping for known blocks).
//
// We walk the 4 cardinal tiles around the machine's footprint perimeter and
// check for the target tile type (matching upstream's 6-direction check
// projected to 2D).
public sealed class AdjacentBlockCondition : RecipeCondition
{
	public ushort RequiredTileType { get; }
	public int MinCount { get; }

	public AdjacentBlockCondition() : this(0, 1) { }
	public AdjacentBlockCondition(ushort tileType, int minCount) { RequiredTileType = tileType; MinCount = minCount; }

	public override bool Test(RecipeLogic logic)
	{
		if (logic.GetRLMachine() is not MetaMachine mte) return true;
		int count = 0;
		var pos = mte.Position;
		var (w, h) = mte.Size;
		// Walk perimeter cells of the machine footprint.
		for (int dx = -1; dx <= w; dx++)
		{
			Check(pos.X + dx, pos.Y - 1, ref count);
			Check(pos.X + dx, pos.Y + h, ref count);
		}
		for (int dy = 0; dy < h; dy++)
		{
			Check(pos.X - 1,     pos.Y + dy, ref count);
			Check(pos.X + w,     pos.Y + dy, ref count);
		}
		return count >= MinCount;
	}

	private void Check(int x, int y, ref int count)
	{
		if (x < 0 || x >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY) return;
		var tile = Main.tile[x, y];
		if (tile.HasTile && tile.TileType == RequiredTileType) count++;
	}

	public override string GetTooltips() =>
		$"Requires {MinCount}x adjacent tile type #{RequiredTileType}";
	public override string GetTypeName() => "gtceu:adjacent_block";
}
