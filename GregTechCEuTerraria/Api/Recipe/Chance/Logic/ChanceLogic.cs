#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Recipe.Chance.Boost;
using GregTechCEuTerraria.Api.Recipe.Content;

namespace GregTechCEuTerraria.Api.Recipe.Chance.Logic;

// PARTIAL - pure-math port of
// com.gregtechceu.gtceu.api.recipe.chance.logic.ChanceLogic.
// DO NOT modify behavior; mirror upstream changes only.
//
// Strategy for chanced-output rolls. Four canonical instances:
//   - OR    : each entry rolls independently (default).
//   - AND   : all-or-nothing (all entries must succeed).
//   - XOR   : exactly one entry succeeds.
//   - NONE  : never produces (used by recipes that disable chanced outputs).
//
// Documented deferrals (lands with full RecipeLogic port):
//   - Forge/Mojang registry (GTRegistries.CHANCE_LOGICS) dropped - flat
//     static list below. registerName / serializer / codec deferred.
//   - Component getTranslation dropped - string id is enough for now;
//     locale lookup via tML's Language layer when tooltip surface lands.
//   - ChanceBoostFunction-dependent roll variants ported with a stub
//     boost = identity until ChanceBoostFunction is ported.
public abstract class ChanceLogic
{
	public string Name { get; }
	protected ChanceLogic(string name) { Name = name; Register(this); }

	// Verbatim port of upstream's `roll(...)` abstract - returns the entries
	// that succeeded their chance roll, multiplied by `times` for batch
	// processing. The chance cache lets a single content's roll persist
	// fractional progress across cycles so 50% chance over 2 cycles yields
	// exactly 1 result.
	//
	// cap            : the recipe capability the entries belong to (for
	//                  copy-via dispatch).
	// chancedEntries : the candidate Content entries with chance values.
	// recipeTier     : the recipe's voltage tier (for tier-based boost).
	// chanceTier     : the machine's voltage tier (for tier-based boost).
	// cache          : per-capability cache for fractional-progress carryover.
	// times          : how many cycles this roll represents (batch parallel).
	public abstract IReadOnlyList<Recipe.Content.Content> Roll(
		object cap,
		IReadOnlyList<Recipe.Content.Content> chancedEntries,
		ChanceBoostFunction function,
		int recipeTier,
		int chanceTier,
		IDictionary<object, int>? cache,
		int times);

	// === Registry ============================================================
	// MUST be declared BEFORE OR/AND/XOR/NONE - their ctors call Register(this)
	// during the class .cctor, which runs field initializers in source order.
	private static readonly List<ChanceLogic> _registry = new();
	private static void Register(ChanceLogic c) { lock (_registry) _registry.Add(c); }
	public static IReadOnlyList<ChanceLogic> All { get { lock (_registry) return _registry.ToArray(); } }

	// === Built-in strategies ================================================

	public static readonly ChanceLogic OR    = new OrLogic();
	public static readonly ChanceLogic AND   = new AndLogic();
	public static readonly ChanceLogic XOR   = new XorLogic();
	public static readonly ChanceLogic NONE  = new NoneLogic();

	// === Pure-math helpers (verbatim) =======================================

	// Upstream uses 10_000 as the max scale (basis points). Mirrors
	// ChanceLogic.getMaxChancedValue().
	public static int GetMaxChancedValue() => 10_000;

	public static int GetChance(Recipe.Content.Content entry, ChanceBoostFunction function, int recipeTier, int chanceTier)
	{
		return function.GetBoostedChance(entry, recipeTier, chanceTier);
	}

	public static int GetCachedChance(Recipe.Content.Content entry, IDictionary<object, int>? cache) =>
		cache is not null && cache.TryGetValue(entry.Payload, out var v) ? v : 0;

	public static void UpdateCachedChance(object payload, IDictionary<object, int>? cache, int value)
	{
		if (cache is null) return;
		cache[payload] = value;
	}

	// Verbatim port of upstream's chance-check (chance >= max OR
	// random-roll passes).
	private static readonly Random _rng = new();
	public static bool PassesChance(int chance, int maxChance)
	{
		if (chance >= maxChance) return true;
		return _rng.Next(maxChance) < chance;
	}

	// === Concrete strategies ================================================

	private sealed class OrLogic : ChanceLogic
	{
		public OrLogic() : base("or") { }

		// Verbatim port of upstream OR.roll. OR is deterministic for
		// guaranteed multiples (totalChance / maxChance) then random for
		// remaining fractional (totalChance % maxChance) added to the
		// cache so leftover fractional progress carries to next cycle.
		public override IReadOnlyList<Recipe.Content.Content> Roll(
			object cap, IReadOnlyList<Recipe.Content.Content> entries,
			ChanceBoostFunction function, int recipeTier, int chanceTier,
			IDictionary<object, int>? cache, int times)
		{
			var result = new List<Recipe.Content.Content>();
			foreach (var entry in entries)
			{
				int maxChance = entry.MaxChance;
				int newChance = GetChance(entry, function, recipeTier, chanceTier);
				int totalChance = times * newChance;
				int guaranteed = totalChance / maxChance;
				if (guaranteed > 0)
					result.Add(entry.CopyChanced(cap, ContentModifier.Multiplier_(guaranteed)));
				newChance = totalChance % maxChance;

				int cached = GetCachedChance(entry, cache);
				int chance = newChance + cached;
				while (PassesChance(chance, maxChance))
				{
					result.Add(entry);
					chance -= maxChance;
					newChance -= maxChance;
				}
				UpdateCachedChance(entry.Payload, cache, newChance / 2 + cached);
			}
			return result;
		}
	}

	private sealed class AndLogic : ChanceLogic
	{
		public AndLogic() : base("and") { }
		// AND: all must pass; if any fails, return empty. Cache is updated
		// for every entry regardless of overall outcome.
		public override IReadOnlyList<Recipe.Content.Content> Roll(
			object cap, IReadOnlyList<Recipe.Content.Content> entries,
			ChanceBoostFunction function, int recipeTier, int chanceTier,
			IDictionary<object, int>? cache, int times)
		{
			// Roll each entry; if any fails for the full `times`, the whole
			// batch fails. Upstream's impl checks if guaranteed >= times
			// per entry; we replicate.
			var result = new List<Recipe.Content.Content>();
			foreach (var entry in entries)
			{
				int maxChance = entry.MaxChance;
				int newChance = GetChance(entry, function, recipeTier, chanceTier);
				int totalChance = times * newChance;
				if (totalChance / maxChance < times)
				{
					// Insufficient guaranteed for AND - try cache + random.
					int cached = GetCachedChance(entry, cache);
					if (!PassesChance(totalChance + cached, maxChance * times))
						return System.Array.Empty<Recipe.Content.Content>();
					UpdateCachedChance(entry.Payload, cache, 0);
				}
				int guaranteed = totalChance / maxChance;
				if (guaranteed > 0)
					result.Add(entry.CopyChanced(cap, ContentModifier.Multiplier_(guaranteed)));
			}
			return result;
		}
	}

	private sealed class XorLogic : ChanceLogic
	{
		public XorLogic() : base("xor") { }
		// XOR: exactly one passes per cycle. Pick weighted-random.
		public override IReadOnlyList<Recipe.Content.Content> Roll(
			object cap, IReadOnlyList<Recipe.Content.Content> entries,
			ChanceBoostFunction function, int recipeTier, int chanceTier,
			IDictionary<object, int>? cache, int times)
		{
			var result = new List<Recipe.Content.Content>();
			for (int t = 0; t < times; t++)
			{
				int totalWeight = 0;
				foreach (var e in entries) totalWeight += GetChance(e, function, recipeTier, chanceTier);
				if (totalWeight <= 0) continue;
				int roll = _rng.Next(totalWeight);
				int cumulative = 0;
				foreach (var e in entries)
				{
					cumulative += GetChance(e, function, recipeTier, chanceTier);
					if (roll < cumulative) { result.Add(e); break; }
				}
			}
			return result;
		}
	}

	private sealed class NoneLogic : ChanceLogic
	{
		public NoneLogic() : base("none") { }
		public override IReadOnlyList<Recipe.Content.Content> Roll(
			object cap, IReadOnlyList<Recipe.Content.Content> entries,
			ChanceBoostFunction function, int recipeTier, int chanceTier,
			IDictionary<object, int>? cache, int times) =>
				System.Array.Empty<Recipe.Content.Content>();
	}
}
