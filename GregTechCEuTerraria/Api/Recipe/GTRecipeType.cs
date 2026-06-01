#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Recipe.Category;
using GregTechCEuTerraria.Api.Recipe.Chance.Boost;
using GregTechCEuTerraria.Api.Recipe.Lookup;

namespace GregTechCEuTerraria.Api.Recipe;

// STUB - port of com.gregtechceu.gtceu.api.recipe.GTRecipeType.
//
// Identity key for a kind of recipe (macerator recipes, EBF recipes, etc.).
// Upstream is 358 lines with deep dependencies on JEI categories, recipe
// serializer, builder factory, UI layout, slot count config, modifier
// chain, ...
//
// This partial port carries: id (registry name), getCategory() default, and
// the per-recipe-type RecipeDB lookup trie (searchRecipe - see RecipeDB /
// RecipeLookupCompiler under Api/Recipe/Lookup/).
//
// Still stubbed (declared inline on the concrete machines for now): the
// recipe-builder DSL, per-capability slot-count config, JEI/UI surface,
// sound, and the modifier chain.
public sealed class GTRecipeType
{
	public string RegistryName { get; }

	public GTRecipeType(string registryName)
	{
		RegistryName = registryName;
		lock (_registry) _registry[registryName] = this;
	}

	// === Registry =============================================================
	// Mirrors upstream's `GTRegistries.RECIPE_TYPES` lookup but flat. Loader
	// uses GetOrCreate to materialize a recipe type for each unique station
	// id seen in the JSON.
	//
	// MUST be declared before PLACEHOLDER: C# runs static field initializers
	// top-to-bottom, and PLACEHOLDER's constructor inserts into _registry. If
	// _registry is declared after PLACEHOLDER it is still null when PLACEHOLDER
	// is built, so `lock (_registry)` throws and the whole GTRecipeType static
	// initializer fails with TypeInitializationException.
	private static readonly System.Collections.Generic.Dictionary<string, GTRecipeType> _registry = new();

	// Default-constructor placeholder used by GTRecipeCategory.DEFAULT
	// before any real recipe types are registered.
	private GTRecipeType() : this("__placeholder__") { }

	public static readonly GTRecipeType PLACEHOLDER = new();

	public static GTRecipeType GetOrCreate(string registryName)
	{
		lock (_registry)
		{
			if (_registry.TryGetValue(registryName, out var existing)) return existing;
		}
		return new GTRecipeType(registryName); // ctor inserts
	}

	// === Research data-stick entries ========================================
	// Port of GTRecipeType.researchEntries (addDataStickEntry /
	// getDataStickEntry / removeDataStickEntry). Maps a research_id to the
	// recipes it unlocks for THIS recipe type. Populated at recipe-load when a
	// recipe carries a ResearchCondition (see RecipeJsonLoader). The
	// DataAccessHatch reads a data item's research_id and pulls these recipes
	// into its available set; assembly_line recipes with a ResearchCondition
	// are gated on the hatch having the matching entry.
	private readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<GTRecipe>> _researchEntries = new();

	public void AddDataStickEntry(string researchId, GTRecipe recipe)
	{
		lock (_researchEntries)
		{
			if (!_researchEntries.TryGetValue(researchId, out var set))
			{
				set = new System.Collections.Generic.HashSet<GTRecipe>();
				_researchEntries[researchId] = set;
			}
			set.Add(recipe);
		}
	}

	public System.Collections.Generic.IReadOnlyCollection<GTRecipe>? GetDataStickEntry(string researchId)
	{
		lock (_researchEntries)
			return _researchEntries.TryGetValue(researchId, out var set) ? set : null;
	}

	public bool RemoveDataStickEntry(string researchId, GTRecipe recipe)
	{
		lock (_researchEntries)
		{
			if (!_researchEntries.TryGetValue(researchId, out var set)) return false;
			bool removed = set.Remove(recipe);
			if (removed && set.Count == 0) _researchEntries.Remove(researchId);
			return removed;
		}
	}

	public static GTRecipeType? Get(string registryName)
	{
		lock (_registry) return _registry.TryGetValue(registryName, out var t) ? t : null;
	}

	public static System.Collections.Generic.IReadOnlyCollection<GTRecipeType> All
	{
		get { lock (_registry) return new System.Collections.Generic.List<GTRecipeType>(_registry.Values); }
	}

	// Verbatim port of upstream's `getCategory()` - default category for
	// this recipe type. Used by GTRecipe constructor when recipeCategory
	// arg == GTRecipeCategory.DEFAULT.
	public GTRecipeCategory GetCategory() => GTRecipeCategory.DEFAULT;

	// Verbatim port of `chanceFunction` - boosts chanced output yields based
	// on the (chanceTier - recipeTier) overclock difference. Defaults to NONE
	// (no boost); per-station overrides can set OVERCLOCK or a custom function
	// once the recipe-builder DSL lands.
	public ChanceBoostFunction ChanceFunction { get; set; } = ChanceBoostFunction.NONE;

	// === RecipeLookup trie ==================================================
	// Port of upstream's per-GTRecipeType `RecipeDB db`. Built lazily on first
	// SearchRecipe from the station's RecipeRegistry list, and rebuilt if that
	// list's size changes (recipe reload). `_untrieable` holds recipes the
	// compiler can't represent as trie keys (NBT-predicate ingredients,
	// unresolved tags, recipes with no searchable input) plus any that lost an
	// Add conflict - those are always flat-scanned, so no recipe is ever lost.
	//
	// Single-threaded: Terraria game logic (and thus SearchRecipe) runs on one
	// thread, so the lazy build needs no lock.
	private RecipeDB?       _db;
	private List<GTRecipe>? _untrieable;
	private int             _dbBuiltAtCount = -1;

	private void EnsureDb()
	{
		int count = TerrariaCompat.Recipes.RecipeRegistry.Count;
		if (_db != null && _dbBuiltAtCount == count) return;

		var db         = new RecipeDB();
		var untrieable = new List<GTRecipe>();
		foreach (var r in TerrariaCompat.Recipes.RecipeRegistry.ForStation(RegistryName))
		{
			var lists = RecipeLookupCompiler.TryCompileRecipe(r);
			// null              -> an ingredient has no trie key
			// empty             -> recipe has no searchable input at all
			// Add returns false -> lost an exact-path conflict to an earlier recipe
			if (lists == null || lists.Count == 0 || !db.Add(r, lists))
				untrieable.Add(r);
		}
		_db             = db;
		_untrieable     = untrieable;
		_dbBuiltAtCount = count;
	}

	// Port of upstream `searchRecipe(IRecipeCapabilityHolder, Predicate<GTRecipe>)
	// : Iterator<GTRecipe>`. Yields every recipe at this station whose input
	// ingredients are all available on `holder`, passing `filter`.
	//
	// A holder that opts out of the lookup trie (SupportsRecipeLookup == false)
	// gets the flat per-station scan - identical results, no trie speedup.
	// For an opted-in holder the trie iterator yields the candidates and the
	// `_untrieable` fallback list is always also scanned; results are de-duped
	// - our compile-time tag expansion can index one recipe under several
	// paths, so the iterator may surface it more than once (upstream's
	// tag-keyed trie does not, hence upstream has no de-dup).
	public IEnumerable<GTRecipe> SearchRecipe(
		Machine.Feature.IRecipeLogicMachine holder,
		System.Predicate<GTRecipe> filter)
	{
		if (!holder.SupportsRecipeLookup)
		{
			foreach (var r in TerrariaCompat.Recipes.RecipeRegistry.ForStation(RegistryName))
				if (filter(r)) yield return r;
			yield break;
		}

		EnsureDb();
		var query = RecipeLookupCompiler.CompileQuery(holder);
		var seen  = new HashSet<GTRecipe>();

		var iter = new RecipeDB.RecipeIterator(_db!, query, filter);
		while (iter.HasNext())
		{
			var r = iter.Next();
			if (seen.Add(r)) yield return r;
		}
		foreach (var r in _untrieable!)
			if (seen.Add(r) && filter(r)) yield return r;
	}

	// Resolve a recipe by its id. Used by RecipeLogic to re-attach the running
	// recipe after a world load / MP join - only the id (LastRecipeId) round-
	// trips, not the GTRecipe object, so a machine loaded mid-recipe must look
	// its recipe back up or it restarts at progress 0.
	public GTRecipe? GetRecipeById(string id)
	{
		foreach (var r in TerrariaCompat.Recipes.RecipeRegistry.ForStation(RegistryName))
			if (r.Id == id) return r;
		return null;
	}

	// === Capability presence ================================================
	// Derived lazily from the loaded recipe set. Mirrors upstream's
	// `getMaxInputs(cap) > 0` / `getMaxOutputs(cap) > 0` - the bit
	// `Predicates.autoAbilities(recipeType)` consults to decide which hatch
	// abilities to require. Our `GTRecipeType` doesn't carry a configured
	// per-capability slot count yet (the recipe-builder DSL isn't ported), so
	// we read it back from what's actually in `RecipeRegistry` for this
	// station. Reads as `false` on a station with zero recipes; recomputes
	// when the registry size changes (reload).
	private HashSet<object>? _inputCaps;
	private HashSet<object>? _outputCaps;
	private int              _capsBuiltAtCount = -1;

	private void EnsureCaps()
	{
		int count = TerrariaCompat.Recipes.RecipeRegistry.Count;
		if (_inputCaps != null && _capsBuiltAtCount == count) return;
		var ins  = new HashSet<object>();
		var outs = new HashSet<object>();
		foreach (var r in TerrariaCompat.Recipes.RecipeRegistry.ForStation(RegistryName))
		{
			foreach (var k in r.Inputs.Keys)      ins.Add(k);
			foreach (var k in r.TickInputs.Keys)  ins.Add(k);
			foreach (var k in r.Outputs.Keys)     outs.Add(k);
			foreach (var k in r.TickOutputs.Keys) outs.Add(k);
		}
		_inputCaps        = ins;
		_outputCaps       = outs;
		_capsBuiltAtCount = count;
	}

	public bool HasInput(object capability)  { EnsureCaps(); return _inputCaps!.Contains(capability); }
	public bool HasOutput(object capability) { EnsureCaps(); return _outputCaps!.Contains(capability); }

	public override string ToString() => $"GTRecipeType{{{RegistryName}}}";
	public override bool Equals(object? obj) => obj is GTRecipeType t && RegistryName == t.RegistryName;
	public override int GetHashCode() => RegistryName.GetHashCode();
}
