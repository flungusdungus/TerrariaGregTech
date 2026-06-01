#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Recipe;

namespace GregTechCEuTerraria.Api.Capability.Recipe;

// LOCKED - port of
// com.gregtechceu.gtceu.api.capability.recipe.IRecipeHandler.
// DO NOT modify behavior; mirror upstream changes only.
//
// A handler that participates in recipe matching. Implementers expose the
// capability they serve (`GetCapability()`) and a `HandleRecipeInner` body
// that drains/fills the K-typed resource against this handler's state.
//
// Adaptations: getContents() -> IReadOnlyList<object> (Java raw List<Object>).
// The non-generic IRecipeHandler parent below replaces upstream's wildcard
// IRecipeHandler<?> - C# generic invariance has no wildcard, so a non-generic
// parent with *Boxed bridges dispatches without reflection.

// Non-generic dispatch surface - lets RecipeHandlerList walk handlers without
// knowing the K type parameter. Concrete handlers implement `IRecipeHandler<K>`
// and inherit these via DIM bridges below.
public interface IRecipeHandler
{
	// Identity token for the capability this handler serves. Returns the
	// `RecipeCapability<K>` instance as a non-generic `IRecipeCapability`.
	IRecipeCapability GetCapabilityRaw();

	// Priority of this handler. Higher fires first during recipe matching.
	int GetPriority();

	// Total amount of resource stored, in capability-defined units.
	double GetTotalContentAmount();

	// True = each input slot is treated as a separate batch instead of pooled.
	bool IsDistinct();

	// Boxed dispatch of `HandleRecipe(io, recipe, left, simulate)`. Returns
	// leftover content (null = fully consumed). Implementations narrow the
	// `IReadOnlyList<object>` to the typed `List<K>` internally - the boxing
	// path is owned by the interface itself (DIM on IRecipeHandler<K>), so
	// callers get a leftover list typed as object regardless of K.
	IReadOnlyList<object>? HandleRecipeBoxed(IO io, GTRecipe recipe,
		IReadOnlyList<object> left, bool simulate);
}

public interface IRecipeHandler<K> : IFilteredHandler<K>, IRecipeHandler
{
	// Verbatim port of upstream's `ENTRY_COMPARATOR` - sorts handlers by
	// priority first, then non-empty storage. RecipeHandlerList uses this
	// to order handlers during recipe-match dispatch so high-priority /
	// pre-filled handlers are tried before empty ones.
	public static readonly System.Collections.Generic.IComparer<IRecipeHandler<K>> ENTRY_COMPARATOR =
		System.Collections.Generic.Comparer<IRecipeHandler<K>>.Create((o1, o2) =>
		{
			// #1: priority (descending via PRIORITY_COMPARATOR semantics).
			// Disambiguate IFilteredHandler<K>.GetPriority vs IRecipeHandler.GetPriority
			// - both inherited via IRecipeHandler<K>, same implementation either way.
			int prio = ((IRecipeHandler)o2).GetPriority().CompareTo(((IRecipeHandler)o1).GetPriority());
			if (prio != 0) return prio;
			// #2: non-empty before empty.
			bool empty1 = o1.GetTotalContentAmount() <= 0;
			bool empty2 = o2.GetTotalContentAmount() <= 0;
			return empty1.CompareTo(empty2);
		});
	// Match or apply the given recipe. Returns leftover content (null = all
	// consumed). Per-handler stateful drain happens here when `simulate` is
	// false. Mirrors upstream method-for-method.
	//
	// io       : IO.IN (consume) or IO.OUT (produce).
	// recipe   : the recipe being matched.
	// left     : recipe contents still needing a handler.
	// simulate : true = compute, don't mutate.
	// returns  : remaining contents the next handler should try (null on
	//            full consumption).
	List<K>? HandleRecipeInner(IO io, GTRecipe recipe, List<K> left, bool simulate);

	// Container size if applicable, otherwise -1. Mirrors upstream default.
	int GetSize() => -1;

	// Cross-capability raw inspection. Used by recipe-browser code and
	// match-priority logic.
	IReadOnlyList<object> GetContents();

	// Total amount of K-resource stored, in capability-defined units.
	// (EnergyStored for EU, sum of stack counts for items, mB for fluids.)
	new double GetTotalContentAmount();

	// True = each input slot is treated as a separate batch instead of
	// pooled. RecipeLogic checks this when matching multi-slot inputs.
	new bool IsDistinct() => false;

	// False suppresses content from the cross-capability search (e.g.
	// circuit slots vs item inventories). Default true.
	bool ShouldSearchContent() => true;

	// Identity token for the capability this handler serves.
	RecipeCapability<K> GetCapability();

	// Default content-copy via the capability's CopyInner. Inputs are
	// arriving as Object because recipe-list typing is erased at that layer.
	K CopyContent(object content) => GetCapability().CopyInner((K)content);

	// Default copy-then-handle path. Recipe-driving code calls this; the
	// `left` list comes in as cross-capability raw objects and we narrow to
	// K before calling HandleRecipeInner.
	List<K>? HandleRecipe(IO io, GTRecipe recipe, IReadOnlyList<object> left, bool simulate)
	{
		var contents = new List<K>(left.Count);
		foreach (var leftObj in left) contents.Add(CopyContent(leftObj));
		return HandleRecipeInner(io, recipe, contents, simulate);
	}

	// === Non-generic IRecipeHandler bridge ===================================
	// Default-impl bridges that route the wildcard-style calls through the
	// typed `IRecipeHandler<K>` surface. Replaces the reflection that
	// RecipeHandlerList used to do per-call.

	IRecipeCapability IRecipeHandler.GetCapabilityRaw() => GetCapability();

	// `IFilteredHandler<K>.GetPriority` already returns int; route through it.
	// Cast disambiguates between the two `GetPriority` slots IRecipeHandler<K>
	// inherits (IFilteredHandler<K> and IRecipeHandler).
	int IRecipeHandler.GetPriority() => ((IFilteredHandler<K>)this).GetPriority();

	double IRecipeHandler.GetTotalContentAmount() => GetTotalContentAmount();

	bool IRecipeHandler.IsDistinct() => IsDistinct();

	IReadOnlyList<object>? IRecipeHandler.HandleRecipeBoxed(IO io, GTRecipe recipe,
		IReadOnlyList<object> left, bool simulate)
	{
		// `HandleRecipe` returns `List<K>?`. C# generic invariance: that list
		// does NOT implement `IReadOnlyList<object>` even for reference K, so
		// the cast would throw `InvalidCastException` the first tick a handler
		// leaves a leftover. Box every element through a fresh `List<object>`.
		var result = HandleRecipe(io, recipe, left, simulate);
		if (result is null) return null;
		var copied = new List<object>(result.Count);
		foreach (var item in result) copied.Add(item!);
		return copied;
	}
}
