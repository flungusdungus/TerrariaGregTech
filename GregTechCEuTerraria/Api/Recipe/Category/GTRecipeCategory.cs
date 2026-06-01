#nullable enable
namespace GregTechCEuTerraria.Api.Recipe.Category;

// PARTIAL - port of
// com.gregtechceu.gtceu.api.recipe.category.GTRecipeCategory.
//
// Recipe-browser category - recipes grouped under a category are shown
// together in the recipe browser (e.g. "Ore Processing", "Smelting", "Crafting
// Components").
//
// Documented deferrals (UI surface; lands with recipe-browser polish):
//   - icon / itemIcon (LDLib IGuiTexture + ItemStack) dropped
//   - codec serialization dropped
//   - registry (GTRegistries.RECIPE_CATEGORIES) dropped - flat static list
//
// Minimum surface ported: id + recipeType reference + DEFAULT sentinel.
public sealed class GTRecipeCategory
{
	public string Name { get; }
	public GTRecipeType RecipeType { get; }
	public string LangKey { get; }

	public GTRecipeCategory(string name, GTRecipeType recipeType, string langKey)
	{
		Name = name;
		RecipeType = recipeType;
		LangKey = langKey;
	}

	// Sentinel category - GTRecipe constructor checks this and falls back to
	// the recipe-type's default category. Matches upstream's
	// `GTRecipeCategory.DEFAULT` constant.
	public static readonly GTRecipeCategory DEFAULT =
		new("default", GTRecipeType.PLACEHOLDER, "gtceu.recipe.default");

	public override string ToString() => $"GTRecipeCategory{{{Name}}}";
	public override bool Equals(object? obj) => obj is GTRecipeCategory c && Name == c.Name;
	public override int GetHashCode() => Name.GetHashCode();
}
