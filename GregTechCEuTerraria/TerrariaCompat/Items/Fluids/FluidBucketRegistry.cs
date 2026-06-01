#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Fluids;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Fluids;

// One FluidBucketItem per registered fluid with HasBucket. Run AFTER
// FluidLoader.RegisterAll (needs FluidRegistry populated).
public static class FluidBucketRegistry
{
	private static readonly Dictionary<string, int> _byFluidId = new();

	public static void Register(Mod mod)
	{
		_byFluidId.Clear();
		foreach (var fluid in FluidRegistry.All)
		{
			if (!fluid.HasBucket) continue;
			var item = new FluidBucketItem(fluid);
			mod.AddContent(item);
			_byFluidId[fluid.Id] = item.Type;
		}
		mod.Logger.Info($"Registered {_byFluidId.Count} fluid buckets.");
	}

	public static int? Get(string fluidId) =>
		_byFluidId.TryGetValue(fluidId, out var type) ? type : null;
}
