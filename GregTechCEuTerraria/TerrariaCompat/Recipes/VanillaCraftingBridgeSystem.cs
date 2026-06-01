#nullable enable
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Recipes;

// Bridges RecipeRegistry into Terraria's vanilla crafting. Machine-station
// recipes stay in the registry and are consumed by WorkableTieredMachine.
public sealed class VanillaCraftingBridgeSystem : ModSystem
{
	public override void AddRecipeGroups()
	{
		// Eager registration - bridge tracks catalyst group ids for the
		// not-consumed callback. Other tags resolve lazily (TryResolveGroup).
		ToolRecipeGroups.Register();
	}

	public override void AddRecipes()
	{
		VanillaCraftingBridge.Register(Mod);
		AddCompatHandRecipes();
	}

	// Pass B - runs after Main.recipe[] is fully populated.
	public override void PostAddRecipes()
	{
		NativeRecipeProxy.SynthesizeFromTerrariaRecipes();
	}

	// Early-game progression fix: upstream gates wood rods + plates behind
	// Lathe/Bender, so a fresh world has no bare-hands path -> softlock. Two
	// hand recipes (no tile) unblock them. Registered as Terraria recipes -
	// not through the bridge - because the bridge collapses tags to single
	// items and can't express the vanilla Wood RecipeGroup.
	private void AddCompatHandRecipes()
	{
		if (Mod.TryFind<ModItem>("wood_rod", out var rod))
			Terraria.Recipe.Create(rod.Type, 4)
				.AddRecipeGroup(RecipeGroupID.Wood, 1)
				.Register();

		if (Mod.TryFind<ModItem>("wood_plate", out var plate))
			Terraria.Recipe.Create(plate.Type, 2)
				.AddRecipeGroup(RecipeGroupID.Wood, 1)
				.Register();

		// Clay block -> 4 clay balls by hand (verbatim MC) - upstream gates
		// clay balls behind the LV Extractor, blocking the ceramic/fireclay chain.
		if (Items.MaterialItemRegistry.TryGetByUpstreamId("gtceu:clay_gem", out var clayBall))
			Terraria.Recipe.Create(clayBall, 4)
				.AddIngredient(ItemID.ClayBlock, 1)
				.Register();

		// ItemID.Coal is the Christmas Lump of Coal (maxStack=1, gag item);
		// workbench converts to GT coal so it's a real fuel.
		if (Items.MaterialItemRegistry.TryGetByUpstreamId("gtceu:coal_gem", out var coalGem))
			Terraria.Recipe.Create(coalGem, 1)
				.AddIngredient(ItemID.Coal, 1)
				.AddTile(TileID.WorkBenches)
				.Register();

		AddVanillaOreToRawOreRecipes();
	}

	// Vanilla pre-HM ore -> GT raw at 1 : OreTileRegistry.RawOrePerBlock. Closes
	// the iron/copper/gold drop gap (their raw item is upstream's
	// minecraft:raw_<m>) AND makes worldgen ores feed the standard GT chain.
	// By-hand so it bootstraps before a workbench. Tungsten has no GT ORE form,
	// so TungstenOre folds to raw_tungstate.
	private void AddVanillaOreToRawOreRecipes()
	{
		void Add(string materialId, string prefix, int vanillaItemId)
		{
			int? type = Items.MaterialItemRegistry.Get(materialId, prefix);
			if (type is null || type <= 0) return;
			Terraria.Recipe.Create(type.Value, Tiles.OreTileRegistry.RawOrePerBlock)
				.AddIngredient(vanillaItemId, 1)
				.Register();
		}

		Add("iron",     "raw_ore", ItemID.IronOre);
		Add("lead",     "raw_ore", ItemID.LeadOre);
		Add("copper",   "raw_ore", ItemID.CopperOre);
		Add("tin",      "raw_ore", ItemID.TinOre);
		Add("gold",     "raw_ore", ItemID.GoldOre);
		Add("platinum", "raw_ore", ItemID.PlatinumOre);
		Add("silver",   "raw_ore", ItemID.SilverOre);
		Add("tungstate", "raw_ore", ItemID.TungstenOre);
	}
}
