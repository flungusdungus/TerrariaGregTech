#nullable enable
using System.Collections.Generic;

namespace GregTechCEuTerraria.Api.Capability.Recipe;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.capability.recipe.IFilteredHandler.
// DO NOT modify behavior; mirror upstream changes only.
//
// Generic predicate + priority surface used by IRecipeHandler to gate
// ingredient acceptance and sort handlers during recipe matching.
//
// Documented adaptation:
//   - Upstream extends `Predicate<K>`; C# has no equivalent built-in
//     functional interface contract, so we expose `Test(K)` as a default
//     method. Same semantics.
//   - `PRIORITY_COMPARATOR` is exposed as a static IComparer<>; upstream uses
//     `Comparator<IFilteredHandler<?>>` (Java wildcard). C# generic variance
//     doesn't allow exact wildcard semantics, so consumers compare by
//     `GetPriority()` directly.
public interface IFilteredHandler<K>
{
	// Priority constants - matches upstream's per-handler ordering tiers.
	public const int HIGHEST = int.MaxValue;
	public const int HIGH    = int.MaxValue / 2;
	public const int NORMAL  = 0;
	public const int LOW     = int.MinValue / 2;
	public const int LOWEST  = int.MinValue;

	// Test an ingredient for filtering / priority. Default accepts all.
	bool Test(K ingredient) => true;

	// Priority of this handler. Higher fires first during recipe matching.
	int GetPriority() => NORMAL;
}

// IComparer<> wrapper exposing upstream's PRIORITY_COMPARATOR. Sorts by
// priority descending.
public static class FilteredHandlerComparer
{
	public static IComparer<IFilteredHandler<K>> ByPriority<K>() =>
		Comparer<IFilteredHandler<K>>.Create((a, b) => b.GetPriority().CompareTo(a.GetPriority()));
}
