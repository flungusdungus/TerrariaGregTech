#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Recipe.Ingredient.Nbtpredicate;
using GregTechCEuTerraria.Api.Util.ValueProviders;

namespace GregTechCEuTerraria.Api.Recipe.Ingredient;

// LOCKED - JSON dispatch hub for upstream's recipe ingredient schema.
//
// Reads upstream's native JSON ingredient form into a concrete Ingredient
// subclass. The dispatch matches upstream's wire format produced by
// GTRecipeSerializer + IIngredientSerializer + Forge's CraftingHelper:
//
//   {"item": "modid:itemname"}                 -> ItemStackIngredient
//   {"tag":  "forge:ingots/iron"}              -> TagIngredient
//   {"type": "gtceu:circuit", ...}             -> IntCircuitIngredient
//   {"type": "gtceu:sized", ...}               -> SizedIngredient
//   {"type": "gtceu:int_provider", ...}        -> IntProviderIngredient
//   {"type": "gtceu:nbt_predicate", ...}       -> NBTPredicateIngredient
//   {"type": "gtceu:fluid", ...}               -> FluidIngredient
//   {"type": "gtceu:int_provider_fluid", ...}  -> IntProviderFluidIngredient
//   {"type": "gtceu:fluid_container", ...}      -> FluidContainerIngredient
//
// Adaptations:
//   - Mojang Codec / Forge IIngredientSerializer dispatch -> string switch.
//   - Item / tag / fluid id resolution delegated to IIngredientResolver
//     (mod layer; touches IngredientResolver + VanillaSubstitution +
//     FluidRegistry).
public static class IngredientJson
{
	public static Ingredient Read(JsonElement el, IIngredientResolver resolver)
	{
		if (el.ValueKind != JsonValueKind.Object)
			throw new JsonException($"Ingredient expected object, got {el.ValueKind}");

		// `type` field - explicit discriminator. Strip "minecraft:" / "gtceu:"
		// / "forge:" prefixes so callers can use bare names.
		if (el.TryGetProperty("type", out var typeEl))
		{
			string type = typeEl.GetString() ?? "";
			type = StripNs(type);
			switch (type)
			{
				case "circuit":            return ReadIntCircuit(el);
				case "sized":              return ReadSized(el, resolver);
				case "int_provider":       return ReadIntProvider(el, resolver);
				case "nbt_predicate":      return ReadNBTPredicate(el, resolver);
				case "nbt":                return ReadNBTPredicate(el, resolver); // forge:nbt
				case "fluid":              return ReadFluidIngredient(el, resolver);
				case "int_provider_fluid": return ReadIntProviderFluid(el, resolver);
				case "fluid_container":    return ReadFluidContainer(el, resolver);
				case "item":               return ReadItem(el, resolver);
				case "tag":                return ReadTag(el, resolver);
				// Unknown type - fall through to shape dispatch below.
			}
		}

		// Shape-based fallback (no `type` field).
		if (el.TryGetProperty("item", out _))  return ReadItem(el, resolver);
		if (el.TryGetProperty("tag",  out _))  return ReadTag(el, resolver);
		if (el.TryGetProperty("fluid",out _))  return ReadFluidIngredient(el, resolver);

		throw new JsonException($"Unable to identify ingredient type in {el}");
	}

	private static string StripNs(string s)
	{
		int idx = s.IndexOf(':');
		return idx < 0 ? s : s[(idx + 1)..];
	}

	// === Concrete readers ===================================================

	private static ItemStackIngredient ReadItem(JsonElement el, IIngredientResolver resolver)
	{
		string id = el.GetProperty("item").GetString() ?? "";
		int type = resolver.ResolveItemType(id);
		return new ItemStackIngredient(type, id);
	}

	private static TagIngredient ReadTag(JsonElement el, IIngredientResolver resolver)
	{
		string tag = el.GetProperty("tag").GetString() ?? "";
		var resolved = resolver.ResolveItemTag(tag);
		return new TagIngredient(tag, resolved);
	}

	private static IntCircuitIngredient ReadIntCircuit(JsonElement el)
	{
		int configuration = el.GetProperty("configuration").GetInt32();
		return IntCircuitIngredient.Of(configuration);
	}

	private static SizedIngredient ReadSized(JsonElement el, IIngredientResolver resolver)
	{
		// Upstream wire format uses `count` (NOT `amount`) for the wrapped
		// stack size. Verified against `./gradlew runData` output -
		// `{"type":"gtceu:sized","count":N,"ingredient":{...}}`.
		int amount = el.TryGetProperty("count", out var amtEl) ? amtEl.GetInt32()
		           : el.TryGetProperty("amount", out var altEl) ? altEl.GetInt32()
		           : 1;
		var inner = Read(el.GetProperty("ingredient"), resolver);
		return SizedIngredient.Create(inner, amount);
	}

	private static IntProviderIngredient ReadIntProvider(JsonElement el, IIngredientResolver resolver)
	{
		var inner = Read(el.GetProperty("ingredient"), resolver);
		var count = IntProviderJson.Read(el.GetProperty("count"));
		return IntProviderIngredient.Of(inner, count);
	}

	private static NBTPredicateIngredient ReadNBTPredicate(JsonElement el, IIngredientResolver resolver)
	{
		string itemId = el.GetProperty("item").GetString() ?? "";
		// Capture the raw SNBT payload - currently used to NBT-resolve the
		// concrete ItemID for canonical-id ingredients whose per-stack
		// variants are registered as distinct items (today: turbine_rotor).
		string? outputNbt = el.TryGetProperty("nbt", out var nbtEl) && nbtEl.ValueKind == JsonValueKind.String
			? nbtEl.GetString() : null;
		// NBT-aware item resolution. When an `nbt` field is present and the
		// hook resolves the (id, nbt) pair to a concrete ItemID, use that
		// directly; otherwise fall back to the regular resolver.
		int itemType = 0;
		if (outputNbt != null && NBTPredicateIngredient.ResolveItemTypeFromNbt is { } nbtHook)
			itemType = nbtHook(itemId, outputNbt);
		if (itemType == 0)
			itemType = resolver.ResolveItemType(itemId);
		// Predicate deserialization deferred - full NBTPredicate hierarchy
		// (And/Or/Not/HasKey/EqualsValue) lands when we hit recipes that
		// actually use it. For now, always-true predicate (degrades to
		// ItemStackIngredient behavior, which is upstream's default when
		// no predicate is specified).
		NBTPredicate predicate = NBTPredicateIngredient.ALWAYS_TRUE;
		return NBTPredicateIngredient.Of(itemType, predicate, itemId, outputNbt);
	}

	// Verbatim port of upstream `FluidIngredient.fromJson`. The recipe wire
	// shape for a fluid-capability content payload is
	//   {"amount": N, "value": <value>}
	// where <value> is an object {"fluid"|"tag": id}, an array of such objects
	// (upstream OR's them), or a bare string ("id" / "#tag"). A `count_provider`
	// key instead routes to IntProviderFluidIngredient (ranged amount).
	//
	// Public because GTRecipeSerializer dispatches fluid-capability content
	// straight here (upstream uses FluidIngredient.CODEC for the fluid cap) -
	// it is NOT reachable through the item-side `Read` shape dispatch.
	public static Ingredient ReadFluidIngredient(JsonElement el, IIngredientResolver resolver)
	{
		if (el.ValueKind != JsonValueKind.Object)
			throw new JsonException($"Fluid ingredient expected object, got {el.ValueKind}");

		// count_provider -> ranged-amount fluid ingredient.
		if (el.TryGetProperty("count_provider", out _))
			return ReadIntProviderFluid(el, resolver);

		int amount = el.TryGetProperty("amount", out var amtEl) ? amtEl.GetInt32() : 0;

		if (!el.TryGetProperty("value", out var valueEl))
			throw new JsonException($"Fluid ingredient needs a `value` field: {el}");

		return valueEl.ValueKind switch
		{
			JsonValueKind.Object => ReadFluidValue(valueEl, amount, resolver),
			JsonValueKind.Array  => ReadFluidValueArray(valueEl, amount, resolver),
			JsonValueKind.String => ReadFluidValueString(valueEl.GetString() ?? "", amount, resolver),
			_ => throw new JsonException($"Fluid ingredient `value` must be an object, array or string: {el}"),
		};
	}

	// Port of upstream `FluidIngredient.valueFromJson` - a single {"fluid"} or
	// {"tag"} object - combined with the `fromValue` step that wraps it.
	private static FluidIngredient ReadFluidValue(JsonElement valueObj, int amount, IIngredientResolver resolver)
	{
		bool hasFluid = valueObj.TryGetProperty("fluid", out var fluidEl);
		bool hasTag   = valueObj.TryGetProperty("tag",   out var tagEl);
		if (hasFluid && hasTag)
			throw new JsonException("A fluid ingredient entry is either a tag or a fluid, not both");
		if (hasFluid)
		{
			string id = fluidEl.GetString() ?? "";
			var resolved = resolver.ResolveFluidType(id);
			// Unresolved fluid -> empty ingredient; the recipe still loads
			// (mirrors upstream's BuiltInRegistries.FLUID.get returning the
			// empty fluid). It just won't match until the FluidType is registered.
			return resolved is not null
				? new FluidIngredient(resolved, amount)
				: new FluidIngredient(tagName: id, System.Array.Empty<FluidType>(), amount);
		}
		if (hasTag)
		{
			string tag = tagEl.GetString() ?? "";
			return new FluidIngredient(tag, resolver.ResolveFluidTag(tag), amount);
		}
		throw new JsonException($"A fluid ingredient entry needs either a `tag` or a `fluid`: {valueObj}");
	}

	// Array `value` - upstream OR's the values. Our FluidIngredient models a
	// single value plus a resolved fluid list, so the values are flattened into
	// one combined list (membership-equivalent for recipe matching).
	private static FluidIngredient ReadFluidValueArray(JsonElement arr, int amount, IIngredientResolver resolver)
	{
		var fluids = new List<FluidType>();
		var names  = new List<string>();
		foreach (var ve in arr.EnumerateArray())
		{
			var sub = ReadFluidValue(ve, amount, resolver);
			fluids.AddRange(sub.GetFluids());
			names.Add(sub.TagName ?? sub.ExactType?.Id ?? "?");
		}
		return new FluidIngredient(string.Join("|", names), fluids, amount);
	}

	// String `value` - "#tag" is a tag, anything else a fluid id.
	private static FluidIngredient ReadFluidValueString(string value, int amount, IIngredientResolver resolver)
	{
		if (value.StartsWith('#'))
		{
			string tag = value[1..];
			return new FluidIngredient(tag, resolver.ResolveFluidTag(tag), amount);
		}
		var resolved = resolver.ResolveFluidType(value);
		return resolved is not null
			? new FluidIngredient(resolved, amount)
			: new FluidIngredient(tagName: value, System.Array.Empty<FluidType>(), amount);
	}

	// Port of FluidContainerIngredient.SERIALIZER.parse - the `fluid` field is
	// a nested FluidIngredient describing the required container contents.
	private static FluidContainerIngredient ReadFluidContainer(JsonElement el, IIngredientResolver resolver)
	{
		var fluid = (FluidIngredient)ReadFluidIngredient(el.GetProperty("fluid"), resolver);
		return new FluidContainerIngredient(fluid);
	}

	// Port of upstream `IntProviderFluidIngredient.fromJson` - `inner` is a
	// nested fluid ingredient, `count_provider` the IntProvider for the
	// ranged amount.
	private static IntProviderFluidIngredient ReadIntProviderFluid(JsonElement el, IIngredientResolver resolver)
	{
		var inner = (FluidIngredient)ReadFluidIngredient(el.GetProperty("inner"), resolver);
		var count = IntProviderJson.Read(el.GetProperty("count_provider"));
		return IntProviderFluidIngredient.Of(inner, count);
	}
}
