#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Utils;

// LOCKED - verbatim port of com.gregtechceu.gtceu.utils.ISubscription.
// DO NOT modify behavior; mirror upstream changes only.
//
// Single-method functional interface - the handle returned by listener-add
// APIs. Mirrors Java's `@FunctionalInterface`. Callers stash the returned
// instance and invoke Unsubscribe() to stop receiving callbacks.
public interface ISubscription
{
	void Unsubscribe();
}
