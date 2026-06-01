#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace GregTechCEuTerraria.Api.Util.ValueProviders;

// LOCKED - port of Mojang Codec<IntProvider> dispatch logic, adapted to
// System.Text.Json.
//
// Upstream uses Mojang's Codec for IntProvider deserialization. The wire
// shape supports two forms:
//
// 1. Bare integer - interpreted as ConstantInt:
//      "count": 5
//
// 2. Typed object - `type` discriminator dispatches to the concrete class:
//      "count": { "type": "minecraft:uniform", "min_inclusive": 2, "max_inclusive": 6 }
//      "count": { "type": "minecraft:biased_to_bottom", "min_inclusive": 1, "max_inclusive": 4 }
//      "count": { "type": "minecraft:weighted_list", "distribution": [
//          { "data": <IntProvider>, "weight": 3 },
//          { "data": <IntProvider>, "weight": 1 } ] }
//      "count": { "type": "minecraft:constant", "value": 5 }
//
// The discriminator is omitted for "minecraft:" namespace by upstream convention;
// we accept both fully-qualified ("minecraft:uniform") and bare ("uniform")
// forms for safety.
public static class IntProviderJson
{
	public static IntProvider Read(JsonElement element)
	{
		// Bare integer -> ConstantInt.
		if (element.ValueKind == JsonValueKind.Number)
			return new ConstantInt(element.GetInt32());

		if (element.ValueKind != JsonValueKind.Object)
			throw new JsonException(
				$"IntProvider expected a number or object, got {element.ValueKind}");

		string type = element.TryGetProperty("type", out var typeElem)
			? typeElem.GetString() ?? "minecraft:constant"
			: "minecraft:constant";

		// Strip "minecraft:" prefix to handle both forms.
		if (type.StartsWith("minecraft:")) type = type["minecraft:".Length..];

		return type switch
		{
			"constant"          => ReadConstant(element),
			"uniform"           => ReadUniform(element),
			"biased_to_bottom"  => ReadBiasedToBottom(element),
			"weighted_list"     => ReadWeightedList(element),
			_ => throw new JsonException($"Unknown IntProvider type: {type}"),
		};
	}

	private static ConstantInt ReadConstant(JsonElement obj)
	{
		// Upstream allows either nested `value: N` or the whole object being
		// a bare number (handled in Read).
		int value = obj.TryGetProperty("value", out var v) ? v.GetInt32() : 0;
		return new ConstantInt(value);
	}

	private static UniformInt ReadUniform(JsonElement obj)
	{
		int min = obj.GetProperty("min_inclusive").GetInt32();
		int max = obj.GetProperty("max_inclusive").GetInt32();
		return new UniformInt(min, max);
	}

	private static BiasedToBottomInt ReadBiasedToBottom(JsonElement obj)
	{
		int min = obj.GetProperty("min_inclusive").GetInt32();
		int max = obj.GetProperty("max_inclusive").GetInt32();
		return new BiasedToBottomInt(min, max);
	}

	private static WeightedListInt ReadWeightedList(JsonElement obj)
	{
		var entries = new List<(IntProvider, int)>();
		foreach (var item in obj.GetProperty("distribution").EnumerateArray())
		{
			var inner = Read(item.GetProperty("data"));
			int weight = item.TryGetProperty("weight", out var w) ? w.GetInt32() : 1;
			entries.Add((inner, weight));
		}
		return new WeightedListInt(entries);
	}
}
