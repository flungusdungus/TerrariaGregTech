#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Utils;

namespace GregTechCEuTerraria.Api.Machine.Trait;

// LOCKED - port of
// com.gregtechceu.gtceu.api.machine.trait.NotifiableRecipeHandlerTrait.
// DO NOT modify behavior; mirror upstream only.
//
// Generic over T - the recipe-element type this handler accepts (EnergyStack,
// ItemStack-analogue, FluidStack, ...). Mirrors upstream's
// `NotifiableRecipeHandlerTrait<T> extends MachineTrait implements IRecipeHandlerTrait<T>`.
//
// Owns:
//   - listener list - RecipeLogic subscribes "energy changed" / "fluid added"
//     to retry recipe matching when state shifts.
//   - isDistinct flag - RecipeLogic-side feature: when true, treat each input
//     slot as a separate batch instead of pooling. Used by multiblocks.
//
// HandleRecipeInner / GetCapability / GetContents / GetTotalContentAmount /
// GetHandlerIO are left abstract - concrete subclasses (NotifiableEnergyContainer,
// NotifiableFluidTank, NotifiableItemStackHandler) implement.
//
// Adapted: upstream's @SaveField / @SyncToClient / syncDataHolder are dropped
// (we use MachineStateSyncPacket for sync).

// Non-generic surface for RecipeHandlerList - lets it call SetDistinct /
// NotifyListeners on stored wildcard handlers without reflection or a fragile
// `Name.StartsWith("Notifiable")` runtime check. The notifiable subclass below
// implements this; future non-notifiable recipe-handler traits simply don't.
public interface INotifiableRecipeHandler
{
	void SetDistinct(bool distinct);
	void NotifyListeners();
	ISubscription AddChangedListener(Action listener);
}

public abstract class NotifiableRecipeHandlerTrait<T> : MachineTrait, IRecipeHandlerTrait<T>, INotifiableRecipeHandler
{
	protected readonly List<Action> _listeners = new();

	public bool IsDistinct { get; private set; }

	public void SetDistinct(bool distinct)
	{
		IsDistinct = distinct;
		// Upstream marks syncDataHolder dirty here; we don't have that
		// pipeline. Sync is picked up by MachineStateSyncPacket via the
		// trait's Save/Load.
	}

	// Verbatim port of upstream's `addChangedListener(Runnable) : ISubscription`.
	// The returned ISubscription removes the listener from the list when its
	// Unsubscribe() is called.
	public ISubscription AddChangedListener(Action listener)
	{
		_listeners.Add(listener);
		return new ListenerSubscription(_listeners, listener);
	}

	public void NotifyListeners()
	{
		// Iterate a snapshot - listeners may unsubscribe during dispatch.
		foreach (var l in _listeners.ToArray()) l();
	}

	// === IRecipeHandler<T> abstract surface =================================
	// Concrete subclasses implement.

	public abstract List<T>? HandleRecipeInner(IO io, GTRecipe recipe, List<T> left, bool simulate);
	public abstract IReadOnlyList<object> GetContents();
	public abstract double GetTotalContentAmount();
	public abstract RecipeCapability<T> GetCapability();
	public abstract IO GetHandlerIO();

	// IRecipeHandler.IsDistinct() default returns false; we override to
	// return this trait's IsDistinct property (matches upstream's @Getter
	// providing both bean-style getter + isDistinct() method).
	bool IRecipeHandler<T>.IsDistinct() => IsDistinct;

	// Single-call-site unsubscription handle.
	private sealed class ListenerSubscription : ISubscription
	{
		private readonly List<Action> _list;
		private readonly Action _listener;
		public ListenerSubscription(List<Action> list, Action listener)
		{
			_list = list;
			_listener = listener;
		}
		public void Unsubscribe() => _list.Remove(_listener);
	}
}
