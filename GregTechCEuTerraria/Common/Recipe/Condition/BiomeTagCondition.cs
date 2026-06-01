#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Recipe.Condition;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria.ID;

namespace GregTechCEuTerraria.Common.Recipe.Condition;

// LOCKED - Terraria-adapted port of
// com.gregtechceu.gtceu.common.recipe.condition.BiomeTagCondition.
//
// Same shape as BiomeCondition but matches against MC biome tags (e.g.
// "minecraft:is_cold", "forge:is_hot") instead of specific biome ids.
//
// Tests against the MACHINE'S position (NOT the player's Zone* flags).
// Unknown tags degrade to true.
public sealed class BiomeTagCondition : RecipeCondition
{
	public string BiomeTag { get; }

	public BiomeTagCondition() : this("") { }
	public BiomeTagCondition(string biomeTag) { BiomeTag = biomeTag; }

	private static readonly ushort[] ColdTiles    = { TileID.SnowBlock, TileID.IceBlock, TileID.SnowBrick };
	private static readonly ushort[] HotTiles     = { TileID.Sand, TileID.Ash, TileID.AshGrass, TileID.HellstoneBrick };
	private static readonly ushort[] JungleTiles  = { TileID.JungleGrass, TileID.Mud };

	public override bool Test(RecipeLogic logic)
	{
		if (logic.GetRLMachine() is not MetaMachine mte) return true;
		int x = mte.Position.X, y = mte.Position.Y;
		return BiomeTag switch
		{
			"minecraft:is_cold"    => LocationZone.ScanForBiomeTiles(x, y, ColdTiles),
			"minecraft:is_hot"     => LocationZone.IsUnderworld(y) || LocationZone.ScanForBiomeTiles(x, y, HotTiles),
			"minecraft:is_jungle"  => LocationZone.ScanForBiomeTiles(x, y, JungleTiles),
			"forge:is_underground" => LocationZone.IsUnderground(y) || LocationZone.IsCavern(y),
			"forge:is_water"       => false,  // no machine-in-water concept; recipe never matches
			_ => true,
		};
	}

	public override string GetTooltips() => $"Requires biome tag: {BiomeTag}";
	public override string GetTypeName() => "gtceu:biome_tag";
}
