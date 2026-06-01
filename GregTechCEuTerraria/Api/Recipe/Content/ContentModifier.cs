#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability.Recipe;

namespace GregTechCEuTerraria.Api.Recipe.Content;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.recipe.content.ContentModifier.
// DO NOT modify behavior; mirror upstream changes only.
//
// Pure-math transform applied to a Content's amount (number * multiplier +
// addition). Used by overclock logic (4x speed -> 4x content), parallels
// (parallelsx content), EBF temperature bonuses (3.5x content for EU
// overclock with steam catalyst), etc.
public readonly record struct ContentModifier(double Multiplier, double Addition)
{
	public static readonly ContentModifier IDENTITY = new(1, 0);

	public static ContentModifier Multiplier_(double multiplier) =>
		multiplier == 1 ? IDENTITY : new ContentModifier(multiplier, 0);

	public static ContentModifier Addition_(double addition) =>
		addition == 0 ? IDENTITY : new ContentModifier(1, addition);

	public int    Apply(int    number) => (int)(number * Multiplier + Addition);
	public long   Apply(long   number) => (long)(number * Multiplier + Addition);
	public float  Apply(float  number) => (float)(number * Multiplier + Addition);
	public double Apply(double number) =>        number * Multiplier + Addition;

	// Verbatim port of `applyContents(Map<RecipeCapability<?>, List<Content>>)`.
	// Returns a copy of the input map with the modifier applied to every
	// Content. IDENTITY short-circuits to a shallow copy.
	public Dictionary<object, List<Content>> ApplyContents(IReadOnlyDictionary<object, List<Content>> contents)
	{
		if (Equals(IDENTITY))
		{
			var shallow = new Dictionary<object, List<Content>>(contents.Count);
			foreach (var kv in contents) shallow[kv.Key] = kv.Value;
			return shallow;
		}
		var result = new Dictionary<object, List<Content>>(contents.Count);
		foreach (var (cap, list) in contents)
		{
			if (list is null || list.Count == 0) continue;
			var copy = new List<Content>(list.Count);
			foreach (var c in list) copy.Add(c.Copy(cap, this));
			result[cap] = copy;
		}
		return result;
	}
}
