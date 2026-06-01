#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Machine.Trait;

namespace GregTechCEuTerraria.Api.Capability.Recipe;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.capability.recipe.IRecipeCapabilityHolder.
// DO NOT modify behavior; mirror upstream changes only.
//
// Interface for any entity (machine, multiblock) that exposes
// IRecipeHandler<?> handlers for recipe matching. RecipeLogic queries this
// to find handlers grouped by IO direction + capability.
//
// Two parallel index shapes:
//   - getCapabilitiesProxy : per-IO list of RecipeHandlerList (preserves
//     RHL grouping for distinct-bus matching).
//   - getCapabilitiesFlat  : per-IO per-capability flat handler list (for
//     fast capability-keyed dispatch - RecipeHelper.handleRecipeIO iterates
//     this).
//
// Both indexes are populated by addHandlerList - pass a RecipeHandlerList,
// it fans out into both shapes.
public interface IRecipeCapabilityHolder
{
	// True if the holder has any registered handler lists.
	bool HasCapabilityProxies() => GetCapabilitiesProxy().Count > 0;

	// Per-IO grouped handler lists. Mutable; the holder owns the map.
	Dictionary<IO, List<RecipeHandlerList>> GetCapabilitiesProxy();

	// Per-IO per-capability flat handler index. Mutable; populated alongside
	// GetCapabilitiesProxy via AddHandlerList.
	Dictionary<IO, Dictionary<object, List<object>>> GetCapabilitiesFlat();

	// Get all RecipeHandlerLists for one IO direction. Empty if none.
	List<RecipeHandlerList> GetCapabilitiesForIO(IO io) =>
		GetCapabilitiesProxy().TryGetValue(io, out var list) ? list : new List<RecipeHandlerList>();

	// Get all flat handlers for one (IO, capability) pair. Empty if none.
	List<object> GetCapabilitiesFlat(IO io, object cap)
	{
		if (!GetCapabilitiesFlat().TryGetValue(io, out var inner)) return new List<object>();
		return inner.TryGetValue(cap, out var list) ? list : new List<object>();
	}

	// Register a new RecipeHandlerList - populates both grouped and flat
	// indexes. Verbatim port of upstream's default impl.
	void AddHandlerList(RecipeHandlerList handlerList)
	{
		if (handlerList == RecipeHandlerList.NO_DATA) return;
		IO io = handlerList.HandlerIO;
		if (!GetCapabilitiesProxy().TryGetValue(io, out var ioList))
		{
			ioList = new List<RecipeHandlerList>();
			GetCapabilitiesProxy()[io] = ioList;
		}
		ioList.Add(handlerList);

		var entrySet = handlerList.HandlerMap;
		if (!GetCapabilitiesFlat().TryGetValue(io, out var inner))
		{
			inner = new Dictionary<object, List<object>>(entrySet.Count);
			GetCapabilitiesFlat()[io] = inner;
		}
		foreach (var entry in entrySet)
		{
			if (!inner.TryGetValue(entry.Key, out var innerList))
			{
				innerList = new List<object>(entry.Value.Count);
				inner[entry.Key] = innerList;
			}
			innerList.AddRange(entry.Value);
		}
	}
}
