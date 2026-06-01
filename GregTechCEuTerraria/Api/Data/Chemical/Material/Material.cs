#nullable enable
using GregTechCEuTerraria.Api.Data.Chemical.Material.Properties;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.Api.Fluids.Store;
using GregTechCEuTerraria.Api.Fluids;
using System.Collections.Generic;

namespace GregTechCEuTerraria.Api.Data.Chemical.Material;

// Mirrors GTCEu's com.gregtechceu.gtceu.api.data.chemical.material.Material.
// Populated from JSON under Data/Materials/*.json by MaterialLoader.
public sealed class Material
{
	public required string Id { get; init; }              // "iron", "annealed_copper"
	public string? Name { get; init; }                    // localization key; defaults to Mods.GregTechCEuTerraria.Materials.<id>
	public uint? Color { get; init; }                     // 0xRRGGBB
	public uint? SecondaryColor { get; init; }
	public string? IconSet { get; init; }                 // upstream MaterialIconSet name: "METALLIC", "SHINY", "RUBY", ...
	public string? Element { get; init; }                 // periodic symbol: "Fe", "Cu", "Al"
	public string? Formula { get; init; }

	// Which physical/visual forms this material has. Driven by upstream builder
	// methods .ingot/.dust/.gem/.ore/.wood/.polymer/.fluid/.liquid/.gas/.plasma.
	public List<string> Forms { get; init; } = new();

	// Upstream MaterialFlag names. NOT YET PORTED - the materials registry dump
	// does not emit flags, so this list is always empty. The field is kept
	// because the verbatim FluidBuilder port references it (PHOSPHORESCENT /
	// STICKY) in its luminosity/viscosity inference - that path is currently
	// inert since Round-2 dumps fluid stats directly. To finish the port, emit
	// the flag set from the material DataProvider.
	public List<string> Flags { get; init; } = new();

	public List<MaterialComponent> Components { get; init; } = new();

	public int? MeltingPointK { get; init; }              // from .liquid(temp) / .liquid(new FluidBuilder().temperature(t))
	public int? BlastTemperatureK { get; init; }          // from .blast(temp, ...) / .blastTemp(temp, ...)
	public string? BlastGasTier { get; init; }            // "LOW" | "MID" | "HIGH" | "HIGHER" | "HIGHEST"

	// Cable / wire properties from upstream .cableProperties(V[tier], amp, loss
	// [, isSuperconductor, criticalTempK]). Drives placeable wire+cable items
	// per Phase B of the cable port. CableTier is one of our VoltageTier names.
	public string? CableTier { get; init; }
	public int? CableAmperage { get; init; }
	public int? CableLoss { get; init; }
	public bool? CableIsSuperconductor { get; init; }
	public int? CableCriticalTempK { get; init; }

	// Tool property from upstream .toolStats(...). Drives the tool port
	// (Api/Item/Tool). Deserialized straight off the `tool` block in
	// materials.json. Null for materials that generate no tools.
	// Material.HasTool() == upstream material.hasProperty(PropertyKey.TOOL).
	public ToolProperty? Tool { get; init; }

	public bool HasTool() => Tool != null;

	// Fluid-pipe property from upstream .fluidPipeProperties(...). Drives the
	// drum fluid filter (a material's drum may only hold fluids its pipe
	// property permits). Deserialized off the `fluidPipe` block; null for
	// materials with no FLUID_PIPE property. == upstream hasProperty(FLUID_PIPE).
	public FluidPipeProperties? FluidPipe { get; init; }

	public bool HasFluidPipe() => FluidPipe != null;

	// Item-pipe property from upstream `.itemPipeProperties(priority, rate)`.
	// Base routing priority + per-second transfer rate; the per-pipe-size
	// multipliers (ItemPipeSizeModifier) get applied on top at placement
	// time inside PipeItem.BuildItemCell.
	// TODO: not yet emitted by DataGenerators.java - extend the dump to
	// write per-material itemPipe blocks, then load here. For now this
	// stays null for every material and PipeItem.BuildItemCell falls back
	// to upstream's parameterless default `(1, 0.25f)`.
	public ItemPipeProperties? ItemPipe { get; init; }

	public bool HasItemPipe() => ItemPipe != null;

	// Rotor property from upstream .rotorStats(power, efficiency, damage, durability).
	// Drives turbine rotor stats (TurbineRotorItem + RotorHolderPartMachine).
	// Deserialized off the `rotor` block in materials.json. Power + efficiency
	// only - damage + durability dropped (no item durability in Terraria, rotor
	// isn't a melee weapon). Null for materials with no ROTOR property.
	public RotorProperty? Rotor { get; init; }

	public bool HasRotor() => Rotor != null;

	// Records features we recognized but haven't ported yet (toolStats, cable,
	// hazard, etc.). Kept in JSON so we can audit coverage without re-running
	// the extractor.
	public List<string> Unported { get; init; } = new();

	// === Fluid property ===========================================================
	//
	// Mirrors upstream `material.getProperty(PropertyKey.FLUID)`. Populated at
	// Mod.Load by FluidLoader for materials whose Forms list contains LIQUID /
	// GAS / PLASMA / FLUID. Null until then (and stays null for materials with
	// no fluid forms).
	//
	// Material.HasFluid()                  == upstream material.hasFluid()
	// Material.GetFluid()                  == upstream material.getFluid()
	// Material.GetFluid(FluidStorageKey)   == upstream material.getFluid(key)
	public FluidProperty? FluidProperty { get; internal set; }

	public bool HasFluid() => FluidProperty?.Get() != null;

	public FluidType? GetFluid() => FluidProperty?.Get();

	public FluidType? GetFluid(FluidStorageKey key) =>
		FluidProperty?.Get(key);

	// Temperature (Kelvin) of this material's fluid form under `key`.
	// Falls back to the primary fluid's temperature when key is null. Returns
	// 0 if the material has no fluid (caller should HasFluid()-guard).
	public int GetFluidTemperature(FluidStorageKey? key = null)
	{
		var fluid = key is null ? GetFluid() : GetFluid(key);
		return fluid?.Temperature ?? 0;
	}

	// Verbatim port of upstream Material.getFluidBuilder - the queued
	// FluidBuilder for a key, used by FluidBuilder's plasma-temperature
	// inference. Valid only during FluidProperty.RegisterFluids (before the
	// queue is consumed).
	public FluidBuilder? GetFluidBuilder(FluidStorageKey key) =>
		FluidProperty?.GetQueuedBuilder(key);

	public FluidBuilder? GetFluidBuilder()
	{
		var prop = FluidProperty;
		if (prop is null) return null;
		var b = prop.PrimaryKey is { } key ? prop.GetQueuedBuilder(key) : null;
		if (b is not null) return b;
		b = prop.GetQueuedBuilder(FluidStorageKey.LIQUID);
		if (b is not null) return b;
		return prop.GetQueuedBuilder(FluidStorageKey.GAS);
	}
}

public sealed record MaterialComponent(string MaterialId, int Amount);
