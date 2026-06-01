#nullable enable
using System.Linq;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles;

// Cross-merges every pair of GregTech ore tile types so that adjacent veins
// of different materials use continuous auto-tile frames at their boundary
// instead of hard edges.
//
// Runs in PostSetupContent rather than SetStaticDefaults because Main.tileMerge
// indices need every ore tile's Type to be finalized first - and a tile's Type
// is only assigned during tML's content-registration sweep, after individual
// SetStaticDefaults calls complete.
public sealed class OreTileSetupSystem : ModSystem
{
	public override void PostSetupContent()
	{
		var types = OreTileRegistry.AllTypes.ToList();
		int pairs = 0;
		for (int i = 0; i < types.Count; i++)
		{
			for (int j = i + 1; j < types.Count; j++)
			{
				Main.tileMerge[types[i]][types[j]] = true;
				Main.tileMerge[types[j]][types[i]] = true;
				pairs++;
			}
		}
		Mod.Logger.Info($"[OreTileSetup] cross-merged {types.Count} GT ore tiles ({pairs} pairs).");
	}
}
