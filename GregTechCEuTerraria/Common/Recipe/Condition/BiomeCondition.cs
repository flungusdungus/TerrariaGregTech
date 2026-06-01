#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Recipe.Condition;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria.ID;

namespace GregTechCEuTerraria.Common.Recipe.Condition;

// LOCKED - Terraria-adapted port of
// com.gregtechceu.gtceu.common.recipe.condition.BiomeCondition.
//
// Recipe runs only when the MACHINE sits in the specified biome. Biome
// detection is via tile-count scan around the machine's world position
// (NOT the local player's Zone* flags - those are wrong in MP and when
// the player is elsewhere).
//
// MC biome ids map to Terraria biome-indicator tile sets via a static
// table. Unknown upstream biome ids degrade to returning true (recipe
// runs without biome gating).
public sealed class BiomeCondition : RecipeCondition
{
	public string BiomeId { get; }

	public BiomeCondition() : this("") { }
	public BiomeCondition(string biomeId) { BiomeId = biomeId; }

	// Terraria biome-indicator tile sets. Match a tile-count threshold via
	// ScanForBiomeTiles to detect the biome at the machine's location.
	private static readonly ushort[] JungleTiles   = { TileID.JungleGrass, TileID.LihzahrdBrick, TileID.Mud };
	private static readonly ushort[] DesertTiles   = { TileID.Sand, TileID.HardenedSand, TileID.Sandstone };
	private static readonly ushort[] SnowTiles     = { TileID.SnowBlock, TileID.IceBlock, TileID.SnowBrick };
	private static readonly ushort[] MushroomTiles = { TileID.MushroomGrass, TileID.MushroomBlock, TileID.MushroomPlants };
	private static readonly ushort[] OceanTiles    = { TileID.Sand, TileID.ShellPile };

	public override bool Test(RecipeLogic logic)
	{
		if (logic.GetRLMachine() is not MetaMachine mte) return true;
		int x = mte.Position.X, y = mte.Position.Y;
		return BiomeId switch
		{
			"minecraft:jungle"    => LocationZone.ScanForBiomeTiles(x, y, JungleTiles),
			"minecraft:desert"    => LocationZone.ScanForBiomeTiles(x, y, DesertTiles),
			"minecraft:nether"    => LocationZone.IsUnderworld(y),
			"minecraft:ocean"     => LocationZone.ScanForBiomeTiles(x, y, OceanTiles),
			"minecraft:mushroom"  => LocationZone.ScanForBiomeTiles(x, y, MushroomTiles, threshold: 40),
			_ when BiomeId.StartsWith("minecraft:snowy_") =>
			                        LocationZone.ScanForBiomeTiles(x, y, SnowTiles),
			_ => true,
		};
	}

	public override string GetTooltips() => $"Requires biome: {BiomeId}";
	public override string GetTypeName() => "gtceu:biome";
}
