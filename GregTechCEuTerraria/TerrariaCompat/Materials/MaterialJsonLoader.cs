#nullable enable
using GregTechCEuTerraria.Api.Data.Chemical.Material;
using GregTechCEuTerraria.Api.Data.Chemical.Material.Properties;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Fluids.Attribute;
using GregTechCEuTerraria.Api.Fluids.Store;
using GregTechCEuTerraria.Common.Materials;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Materials;

// Populates MaterialRegistry from Data/Materials/*.json. Called from Mod.Load()
// so that the registry is available before dynamic content registration runs.
public static class MaterialJsonLoader
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
	};

	public static void Load(Mod mod)
	{
		MaterialRegistry.Clear();

		foreach (string path in mod.GetFileNames())
		{
			if (!path.StartsWith("Data/Materials/") || !path.EndsWith(".json"))
				continue;

			using var stream = mod.GetFileStream(path);
			using var reader = new StreamReader(stream);
			string json = reader.ReadToEnd();

			var entries = JsonSerializer.Deserialize<List<Material>>(json, JsonOptions);
			if (entries is null) continue;

			// Second pass over the same text: the `fluids` array is parsed by
			// hand (into enqueued FluidBuilders on a FluidProperty) rather than
			// deserialized onto Material, keeping Material a clean upstream
			// mirror. Element i lines up with entries[i] - same array, same order.
			using var doc = JsonDocument.Parse(json);
			var elements = doc.RootElement.EnumerateArray().ToList();

			for (int i = 0; i < entries.Count; i++)
			{
				var raw = entries[i];
				var resolved = new Material
				{
					Id = raw.Id,
					Name = raw.Name ?? $"Mods.GregTechCEuTerraria.Materials.{raw.Id}",
					Color = raw.Color,
					SecondaryColor = raw.SecondaryColor,
					IconSet = raw.IconSet,
					Element = raw.Element,
					Formula = raw.Formula,
					Forms = raw.Forms,
					Flags = raw.Flags,
					Components = raw.Components,
					MeltingPointK = raw.MeltingPointK,
					BlastTemperatureK = raw.BlastTemperatureK,
					BlastGasTier = raw.BlastGasTier,
					CableTier = raw.CableTier,
					CableAmperage = raw.CableAmperage,
					CableLoss = raw.CableLoss,
					CableIsSuperconductor = raw.CableIsSuperconductor,
					CableCriticalTempK = raw.CableCriticalTempK,
					Tool = raw.Tool,
					FluidPipe = raw.FluidPipe,
					Rotor = raw.Rotor,
					Unported = raw.Unported,
				};
				resolved.FluidProperty = BuildFluidProperty(elements[i]);
				MaterialRegistry.Register(resolved);
			}
		}

		mod.Logger.Info($"Loaded {MaterialRegistry.All.Count} materials.");
	}

	// Parse the per-material `fluids` array into a FluidProperty with one
	// enqueued FluidBuilder per entry. Mirrors upstream Material.Builder
	// .fluid()/.liquid()/.gas()/.plasma() enqueueing into FluidProperty.
	// The fluids are BUILT later by FluidLoader (FluidProperty.RegisterFluids).
	private static FluidProperty? BuildFluidProperty(JsonElement matEl)
	{
		if (!matEl.TryGetProperty("fluids", out var fluidsEl) || fluidsEl.ValueKind != JsonValueKind.Array)
			return null;

		FluidProperty? prop = null;
		foreach (var fe in fluidsEl.EnumerateArray())
		{
			if (fe.ValueKind != JsonValueKind.Object) continue;
			var key = ResolveKey(GetString(fe, "key"));
			if (key is null) continue;

			var builder = new FluidBuilder();
			if (ParseState(GetString(fe, "state")) is { } state) builder.State(state);
			if (GetInt(fe, "temperature") is { } t)  builder.Temperature(t);
			if (GetInt(fe, "color")       is { } c)  builder.Color((uint)c);
			if (GetBool(fe, "disableColor"))         builder.DisableColor();
			if (GetInt(fe, "density")     is { } d)  builder.Density(d);
			if (GetInt(fe, "luminosity")  is { } l)  builder.Luminosity(l);
			if (GetInt(fe, "viscosity")   is { } v)  builder.Viscosity(v);
			if (GetInt(fe, "burnTime")    is { } bt) builder.BurnTime(bt);
			if (GetBool(fe, "block"))                builder.Block();
			if (GetBool(fe, "disableBucket"))        builder.DisableBucket();
			if (fe.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Array)
				foreach (var ae in attrs.EnumerateArray())
					if (ResolveAttribute(ae.GetString()) is { } attr) builder.Attribute(attr);

			prop ??= new FluidProperty();
			prop.EnqueueRegistration(key, builder);
		}
		return prop;
	}

	private static FluidStorageKey? ResolveKey(string? name) => name?.ToUpperInvariant() switch
	{
		"LIQUID" => FluidStorageKey.LIQUID,
		"GAS"    => FluidStorageKey.GAS,
		"PLASMA" => FluidStorageKey.PLASMA,
		"MOLTEN" => FluidStorageKey.MOLTEN,
		_        => null,
	};

	private static FluidState? ParseState(string? name) => name?.ToUpperInvariant() switch
	{
		"LIQUID" => FluidState.LIQUID,
		"GAS"    => FluidState.GAS,
		"PLASMA" => FluidState.PLASMA,
		_        => null,
	};

	// Only ACID exists upstream; unknown attribute ids are skipped.
	private static FluidAttribute? ResolveAttribute(string? id)
	{
		if (string.IsNullOrEmpty(id)) return null;
		int colon = id.IndexOf(':');
		string bare = colon >= 0 ? id[(colon + 1)..] : id;
		return bare.ToLowerInvariant() == "acid" ? FluidAttributes.ACID : null;
	}

	private static string? GetString(JsonElement el, string key) =>
		el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

	private static int? GetInt(JsonElement el, string key) =>
		el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? (int)v.GetInt64() : null;

	private static bool GetBool(JsonElement el, string key) =>
		el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
}
