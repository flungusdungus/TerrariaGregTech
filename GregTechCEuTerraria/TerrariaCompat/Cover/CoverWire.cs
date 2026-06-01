#nullable enable
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Cover;

// Shared Terraria-wire glue for redstone-emitting covers (detector covers, the
// ender redstone link). A cover "emits a redstone signal" by pulsing the red
// wire on its host machine's footprint - Wiring.TripWire, which itself
// early-returns on a client, so this is server / SP only.
public static class CoverWire
{
	public static void TripFootprint(CoverBehavior cover)
	{
		if (cover.CoverHolder is not MetaMachine machine) return;
		var pos = machine.Position;
		var (w, h) = machine.Size;
		Wiring.TripWire(pos.X, pos.Y, w, h);
	}
}
