#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Utils;

namespace GregTechCEuTerraria.Api.Machine.Trait;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.machine.trait.RecipeHandlerList.
// DO NOT modify behavior; mirror upstream changes only.
//
// A collection of IRecipeHandler<?> entries for one IO direction (IN/OUT/
// BOTH/NONE), grouped by capability. Used by IRecipeCapabilityHolder
// (machines / multiblocks) to expose handlers to RecipeLogic + UI.
//
// Each RHL has:
//   - handlerMap : RecipeCapability -> handlers carrying that resource type
//   - handlerIO  : the direction this RHL participates in (IN/OUT/BOTH/NONE)
//   - color      : per-RHL dye color for cover-based grouping
//   - group      : RecipeHandlerGroup discriminator (UNDYED / dyed / distinct)
//
// Recipe-match dispatch: `handleRecipe(io, recipe, contents, simulate)` walks
// the content map, dispatches each capability's contents to the matching
// handler list, collects leftovers.
//
// Documented adaptations:
//   - Reference2ObjectOpenHashMap -> Dictionary (C# dictionary already uses
//     reference equality on reference-type keys).
//   - Iterator.fastIterator -> standard foreach.
//   - Upstream's `IRecipeHandler<?>` wildcard collapses in C# to the
//     non-generic `IRecipeHandler` interface (DIM-bridged from the typed
//     `IRecipeHandler<K>`); INotifiableRecipeHandler / IRecipeHandlerTrait
//     are the non-generic surfaces for the trait-side concerns. No
//     reflection - replaces an earlier reflection-based dispatch.
public class RecipeHandlerList
{
	public static readonly RecipeHandlerList NO_DATA = new(IO.NONE);

	// Verbatim port of upstream's COMPARATOR. Sorts by priority ascending
	// (lower first), then empty-storage handlers last.
	public static readonly IComparer<RecipeHandlerList> COMPARATOR =
		Comparer<RecipeHandlerList>.Create((h1, h2) =>
		{
			int cmp = h1.GetPriority().CompareTo(h2.GetPriority());
			if (cmp != 0) return cmp;
			bool b1 = h1.GetTotalContentAmount() > 0;
			bool b2 = h2.GetTotalContentAmount() > 0;
			return b1.CompareTo(b2);
		});

	// Public type kept as `Dictionary<object, ...>` so external callers
	// (`WorkableMultiblockMachine`, `IRecipeCapabilityHolder`) don't need
	// touching. Keys are always `IRecipeCapability` at runtime.
	public Dictionary<object, List<object>> HandlerMap { get; } = new();
	private readonly List<IRecipeHandler> _allHandlers = new();
	private readonly List<INotifiableRecipeHandler> _allHandlerTraits = new();

	public IO HandlerIO { get; }
	public int Color { get; private set; } = -1;

	public RecipeHandlerGroup Group { get; set; } = RecipeHandlerGroupColor.UNDYED;

	protected RecipeHandlerList(IO handlerIO) { HandlerIO = handlerIO; }

	// === Factory overloads (verbatim) =======================================

	public static RecipeHandlerList Of(IO io, int color, params object[] handlers)
	{
		var rhl = new RecipeHandlerList(io);
		rhl.AddHandlers(handlers);
		rhl.SetColor(color);
		return rhl;
	}

	public static RecipeHandlerList Of(IO io, params object[] handlers)
	{
		var rhl = new RecipeHandlerList(io);
		rhl.AddHandlers(handlers);
		return rhl;
	}

	public static RecipeHandlerList Of(IO io, IEnumerable<object> handlers)
	{
		var rhl = new RecipeHandlerList(io);
		rhl.AddHandlers(handlers);
		return rhl;
	}

	public static RecipeHandlerList Of(IO io, int color, IEnumerable<object> handlers)
	{
		var rhl = new RecipeHandlerList(io);
		rhl.AddHandlers(handlers);
		rhl.SetColor(color);
		return rhl;
	}

	// === Handler attachment ==================================================
	// Accept `object` for API compatibility, narrow to IRecipeHandler at the
	// boundary. A non-IRecipeHandler argument throws - it would have failed
	// at first dispatch anyway.

	public void AddHandler(object handler) => AddHandlers(new[] { handler });

	public void AddHandlers(params object[] handlers) => AddHandlers((IEnumerable<object>)handlers);

	public void AddHandlers(IEnumerable<object> handlers)
	{
		foreach (var raw in handlers)
		{
			if (raw is not IRecipeHandler handler)
				throw new InvalidOperationException(
					$"Handler {raw.GetType().Name} doesn't implement IRecipeHandler");
			var cap = handler.GetCapabilityRaw();
			if (!HandlerMap.TryGetValue(cap, out var list))
			{
				list = new List<object>();
				HandlerMap[cap] = list;
			}
			list.Add(handler);
			_allHandlers.Add(handler);
			if (handler is INotifiableRecipeHandler nrh) _allHandlerTraits.Add(nrh);
		}
		if (HandlerIO.Supports(IO.OUT)) Sort();
	}

	private void Sort()
	{
		foreach (var list in HandlerMap.Values)
		{
			// IRecipeHandler.ENTRY_COMPARATOR is typed per K; runtime-sort
			// via reflection-free identity comparator on (priority, empty).
			list.Sort((a, b) =>
			{
				var ha = (IRecipeHandler)a;
				var hb = (IRecipeHandler)b;
				int prio = hb.GetPriority().CompareTo(ha.GetPriority());
				if (prio != 0) return prio;
				bool empty1 = ha.GetTotalContentAmount() <= 0;
				bool empty2 = hb.GetTotalContentAmount() <= 0;
				return empty1.CompareTo(empty2);
			});
		}
	}

	// === Distinct grouping ===================================================
	// Verbatim port of setDistinct / setDistinctAndNotify. Switches the group
	// between BUS_DISTINCT and the color-keyed group depending on the flag.

	public void SetDistinctAndNotify(bool distinct) => SetDistinct(distinct, true);
	public void SetDistinct(bool distinct) => SetDistinct(distinct, false);

	protected void SetDistinct(bool distinct, bool notify)
	{
		bool currentDistinct = IsDistinct();
		if (currentDistinct != distinct)
		{
			Group = currentDistinct
				? (RecipeHandlerGroup)new RecipeHandlerGroupColor(Color)
				: RecipeHandlerGroupDistinctness.BUS_DISTINCT;
			foreach (var rht in _allHandlerTraits)
			{
				rht.SetDistinct(distinct);
				if (notify) rht.NotifyListeners();
			}
		}
	}

	public bool IsDistinct() => Group == RecipeHandlerGroupDistinctness.BUS_DISTINCT;

	public void SetColor(int color) => SetColor(color, false);

	public void SetColor(int color, bool notify)
	{
		Color = color;
		if (Group != RecipeHandlerGroupDistinctness.BUS_DISTINCT)
			Group = new RecipeHandlerGroupColor(color);
		if (notify)
		{
			foreach (var rht in _allHandlerTraits)
				rht.NotifyListeners();
		}
	}

	// === Capability accessors ================================================

	public bool HasCapability(object cap) => HandlerMap.ContainsKey(cap);

	public IReadOnlyList<object> GetCapability(object cap) =>
		HandlerMap.TryGetValue(cap, out var list) ? list : Array.Empty<object>();

	public IReadOnlyCollection<object> GetCapabilities() => HandlerMap.Keys;

	// True if any capability in this RHL declares ShouldBypassDistinct.
	// Used by recipe matcher to skip BUS_DISTINCT enforcement for those caps.
	public bool DoesCapabilityBypassDistinct()
	{
		foreach (var cap in GetCapabilities())
			if (cap is IRecipeCapability rc && rc.ShouldBypassDistinct()) return true;
		return false;
	}

	// Compatibility check between this RHL's IO and an external IO direction.
	// Returns false for NO_DATA / NONE; true if direction matches or either
	// side is BOTH.
	public bool IsValid(IO extIO)
	{
		if (this == NO_DATA || HandlerIO == IO.NONE) return false;
		return extIO == IO.BOTH || HandlerIO == IO.BOTH || extIO == HandlerIO;
	}

	// === Priority / amount (sums across all contained handlers) ==============

	public long GetPriority()
	{
		long priority = 0;
		foreach (var handler in _allHandlers) priority += handler.GetPriority();
		return priority;
	}

	public double GetTotalContentAmount()
	{
		double sum = 0;
		foreach (var handler in _allHandlers) sum += handler.GetTotalContentAmount();
		return sum;
	}

	// === Recipe dispatch =====================================================
	// Verbatim port of `handleRecipe(io, recipe, contents, simulate)`.
	// Walks the per-capability content lists, dispatching each list to the
	// matching handler list, collecting leftovers. Returns the leftover map
	// (entries with null leftover are dropped - fully consumed).

	public Dictionary<object, List<object>> HandleRecipe(
		IO io, GTRecipe recipe, Dictionary<object, List<object>> contents, bool simulate)
	{
		if (HandlerMap.Count == 0) return contents;
		var copy = new Dictionary<object, List<object>>(contents);
		// Iterate via key list snapshot (allows mid-iteration removal).
		var keys = new List<object>(copy.Keys);
		foreach (var cap in keys)
		{
			var handlerList = GetCapability(cap);
			var entryValue = copy[cap];
			bool fullyConsumed = false;
			foreach (var handlerObj in handlerList)
			{
				var handler = (IRecipeHandler)handlerObj;
				var left = handler.HandleRecipeBoxed(io, recipe, entryValue, simulate);
				if (left is null) { fullyConsumed = true; break; }
				entryValue = new List<object>(left);
			}
			if (fullyConsumed) copy.Remove(cap);
			else copy[cap] = entryValue;
		}
		return copy;
	}

	public List<object> GetHandlersFlat()
	{
		var list = new List<object>();
		foreach (var kv in HandlerMap) list.AddRange(kv.Value);
		return list;
	}

	// === Listener subscription (verbatim) ====================================
	// Subscribes a listener to every contained notifiable trait. Returns a
	// composite ISubscription that fans out to all child subscriptions.

	private sealed class CompositeSubscription : ISubscription
	{
		private readonly List<ISubscription> _subs;
		public CompositeSubscription(List<ISubscription> subs) { _subs = subs; }
		public void Unsubscribe() { foreach (var s in _subs) s.Unsubscribe(); }
	}

	public ISubscription Subscribe(Action listener)
	{
		var subs = new List<ISubscription>(_allHandlerTraits.Count);
		foreach (var rht in _allHandlerTraits)
		{
			if (rht is IRecipeHandlerTrait t)
				subs.Add(t.AddChangedListener(listener));
		}
		return new CompositeSubscription(subs);
	}

	public ISubscription Subscribe(Action listener, object cap)
	{
		var capList = GetCapability(cap);
		var subs = new List<ISubscription>(capList.Count);
		foreach (var handler in capList)
		{
			if (handler is IRecipeHandlerTrait t)
				subs.Add(t.AddChangedListener(listener));
		}
		return new CompositeSubscription(subs);
	}
}
