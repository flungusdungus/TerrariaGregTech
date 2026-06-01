#nullable enable
using System.Linq;
using GregTechCEuTerraria.Api.Data.Chemical.Material;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Fluids.Store;
using GregTechCEuTerraria.Common.Materials;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Loaders;

// Builds + registers every material's fluids at Mod.Load.
//
// The FluidBuilders were already enqueued onto each material's FluidProperty by
// MaterialJsonLoader (from the JSON `fluids` array - mirrors upstream
// Material.Builder.fluid()/liquid()/gas()/plasma()). This loader does the two
// remaining upstream steps:
//
//   1. mapAlloyBlastProperty - alloys with a blast temperature get an
//      auto-generated MOLTEN fluid (verbatim CommonEventListener).
//   2. registerFluids - build each queued FluidBuilder (running the verbatim
//      Determine* inference) and install the FluidType into FluidRegistry
//      (verbatim FluidStorageImpl.registerFluids, driven per material).
internal static class FluidLoader
{
	public static void RegisterAll(Mod mod)
	{
		// Pass 1 - auto-enqueue MOLTEN for alloys. Must run before any
		// RegisterFluids consumes a queue.
		foreach (var material in MaterialRegistry.All.Values)
			MaybeEnqueueAlloyMolten(material);

		// Pass 2 - build + register.
		int registered = 0;
		foreach (var material in MaterialRegistry.All.Values)
		{
			if (material.FluidProperty is not { } prop) continue;
			prop.RegisterFluids(material);
			registered += prop.Fluids.Count();
		}

		mod.Logger.Info($"Registered {registered} fluids across {MaterialRegistry.All.Count} materials.");
	}

	// Verbatim port of CommonEventListener.mapAlloyBlastProperty: an alloy
	// (>=2 components) that has a blast temperature and a fluid property gets a
	// MOLTEN fluid - unless more than two of its components are fluid-only.
	//
	// Documented deviation: upstream also sets an ALLOY_BLAST material property
	// (drives Alloy Blast Smelter recipes). That machine isn't ported, so the
	// property is omitted; only the MOLTEN fluid is generated.
	private static void MaybeEnqueueAlloyMolten(Material material)
	{
		if (material.Components.Count < 2) return;            // not an alloy
		if (material.BlastTemperatureK is null) return;       // no blast property
		if (material.FluidProperty is not { } prop) return;   // no fluid property
		if (prop.GetQueuedBuilder(FluidStorageKey.MOLTEN) is not null) return;

		// > 2 fluid-only components -> no hot fluid generated (verbatim
		// `components.stream().filter(isMaterialStackFluidOnly).limit(3).count() > 2`).
		int fluidOnly = 0;
		foreach (var comp in material.Components)
		{
			if (IsComponentFluidOnly(comp.MaterialId) && ++fluidOnly > 2)
				return;
		}

		prop.EnqueueRegistration(FluidStorageKey.MOLTEN,
			new FluidBuilder().State(FluidState.LIQUID));
	}

	// Verbatim port of CommonEventListener.isMaterialStackFluidOnly - the
	// component material has a fluid property but no dust form.
	private static bool IsComponentFluidOnly(string componentId) =>
		MaterialRegistry.All.TryGetValue(componentId, out var cm)
		&& !cm.Forms.Contains("DUST")
		&& cm.FluidProperty is not null;
}
