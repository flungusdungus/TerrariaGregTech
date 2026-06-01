#nullable enable
using System;
using System.Collections.Generic;

namespace GregTechCEuTerraria.Api.Util.ValueProviders;

// LOCKED - port of net.minecraft.util.valueproviders.IntProvider.
// DO NOT modify behavior; mirror upstream changes only.
//
// Abstract value-provider for sampling integers - used by recipes (and worldgen
// upstream) to express counts that vary per evaluation. Three modes:
//   - ConstantInt(N)            : always N
//   - UniformInt(lo..hi)        : uniform random in [lo, hi]
//   - BiasedToBottomInt(lo..hi) : two uniform rolls, return min - biased low
//   - WeightedListInt(list)     : sample an IntProvider from a weighted list,
//                                  then recursively sample it
//
// Recipes use IntProvider to give one ingredient a runtime-rolled count
// (e.g. "ore washer outputs 2-6 small dust"). Evaluated fresh each cycle.
//
// Documented adaptations:
//   - Mojang `Codec<IntProvider>` dropped - JSON read goes through our
//     `IntProviderJson.Read(JsonElement)` dispatch (matches the same JSON
//     shape upstream emits via Codec).
//   - `RandomSource` parameter -> `System.Random`. Same uniform distribution.
public abstract class IntProvider
{
	// Verbatim port - return one sampled int.
	public abstract int Sample(Random rng);

	// Verbatim port - bounds for sanity-checking and UI display.
	public abstract int GetMinValue();
	public abstract int GetMaxValue();

	// Type discriminator for JSON serialization. One of:
	//   "minecraft:constant", "minecraft:uniform",
	//   "minecraft:biased_to_bottom", "minecraft:weighted_list"
	public abstract string GetTypeName();
}

// === Concrete: ConstantInt ===================================================

public sealed class ConstantInt : IntProvider
{
	public int Value { get; }
	public ConstantInt(int value) { Value = value; }

	public static readonly ConstantInt ZERO = new(0);

	public override int Sample(Random rng) => Value;
	public override int GetMinValue() => Value;
	public override int GetMaxValue() => Value;
	public override string GetTypeName() => "minecraft:constant";
	public override string ToString() => $"ConstantInt({Value})";
}

// === Concrete: UniformInt ====================================================

public sealed class UniformInt : IntProvider
{
	public int MinInclusive { get; }
	public int MaxInclusive { get; }

	public UniformInt(int minInclusive, int maxInclusive)
	{
		if (minInclusive > maxInclusive)
			throw new ArgumentException(
				$"Empty uniform-int range: min={minInclusive} > max={maxInclusive}");
		MinInclusive = minInclusive;
		MaxInclusive = maxInclusive;
	}

	// Verbatim port of upstream's `Mth.nextInt(rng, min, max)` -
	// inclusive on both ends.
	public override int Sample(Random rng) => rng.Next(MinInclusive, MaxInclusive + 1);
	public override int GetMinValue() => MinInclusive;
	public override int GetMaxValue() => MaxInclusive;
	public override string GetTypeName() => "minecraft:uniform";
	public override string ToString() => $"UniformInt[{MinInclusive}..{MaxInclusive}]";
}

// === Concrete: BiasedToBottomInt =============================================

public sealed class BiasedToBottomInt : IntProvider
{
	public int MinInclusive { get; }
	public int MaxInclusive { get; }

	public BiasedToBottomInt(int minInclusive, int maxInclusive)
	{
		if (minInclusive > maxInclusive)
			throw new ArgumentException(
				$"Empty biased-int range: min={minInclusive} > max={maxInclusive}");
		MinInclusive = minInclusive;
		MaxInclusive = maxInclusive;
	}

	// Verbatim port - `min + min(rng.nextInt(range), rng.nextInt(range))`.
	// Taking the smaller of two uniform rolls biases the distribution toward
	// the minimum.
	public override int Sample(Random rng)
	{
		int range = MaxInclusive - MinInclusive + 1;
		return MinInclusive + Math.Min(rng.Next(range), rng.Next(range));
	}
	public override int GetMinValue() => MinInclusive;
	public override int GetMaxValue() => MaxInclusive;
	public override string GetTypeName() => "minecraft:biased_to_bottom";
	public override string ToString() => $"BiasedToBottomInt[{MinInclusive}..{MaxInclusive}]";
}

// === Concrete: WeightedListInt ===============================================

public sealed class WeightedListInt : IntProvider
{
	// Each entry: (provider, weight). Sample picks one weighted by its
	// weight, then recursively samples that provider.
	public IReadOnlyList<(IntProvider Provider, int Weight)> Entries { get; }
	private readonly int _totalWeight;
	private readonly int _min;
	private readonly int _max;

	public WeightedListInt(IReadOnlyList<(IntProvider, int)> entries)
	{
		if (entries.Count == 0) throw new ArgumentException("Empty weighted list");
		Entries = entries;
		int total = 0;
		int min = int.MaxValue, max = int.MinValue;
		foreach (var (p, w) in entries)
		{
			if (w < 0) throw new ArgumentException($"Negative weight {w}");
			total += w;
			min = Math.Min(min, p.GetMinValue());
			max = Math.Max(max, p.GetMaxValue());
		}
		if (total <= 0) throw new ArgumentException("Total weight must be > 0");
		_totalWeight = total;
		_min = min;
		_max = max;
	}

	public override int Sample(Random rng)
	{
		int roll = rng.Next(_totalWeight);
		int cumulative = 0;
		foreach (var (provider, weight) in Entries)
		{
			cumulative += weight;
			if (roll < cumulative) return provider.Sample(rng);
		}
		// Unreachable if _totalWeight > 0 (guaranteed by ctor).
		return Entries[Entries.Count - 1].Provider.Sample(rng);
	}

	public override int GetMinValue() => _min;
	public override int GetMaxValue() => _max;
	public override string GetTypeName() => "minecraft:weighted_list";
	public override string ToString() => $"WeightedListInt({Entries.Count} entries, [{_min}..{_max}])";
}
