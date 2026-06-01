#nullable enable
using System;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Utils;

namespace GregTechCEuTerraria.Api.Machine.Trait;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.machine.trait.IRecipeHandlerTrait.
// DO NOT modify behavior; mirror upstream changes only.
//
// Marker interface for traits that participate in recipe matching. Combines
// the handler contract (IRecipeHandler<K>) with the trait-specific surface:
//   - GetHandlerIO()       - the trait's role (IN / OUT / BOTH / NONE).
//   - AddChangedListener() - listener subscription. Returns ISubscription
//                            so listeners can later unregister.
//
// The non-generic `IRecipeHandlerTrait` parent below exists so callers that
// don't care about the capability type parameter can still pattern-match
// (`if (trait is IRecipeHandlerTrait rht)`). Upstream Java handles this with
// the raw-type / wildcard pattern (`instanceof IRecipeHandlerTrait<?>`);
// C# generics are invariant and have no wildcard, so the non-generic parent
// is the established workaround. Concrete `IRecipeHandlerTrait<K>` always
// satisfies both via inheritance.
public interface IRecipeHandlerTrait
{
	IO GetHandlerIO();

	// Add a listener that fires when the trait's state changes. Returns an
	// ISubscription handle the caller invokes to stop receiving callbacks.
	ISubscription AddChangedListener(Action listener);
}

public interface IRecipeHandlerTrait<K> : IRecipeHandlerTrait, IRecipeHandler<K>
{
}
