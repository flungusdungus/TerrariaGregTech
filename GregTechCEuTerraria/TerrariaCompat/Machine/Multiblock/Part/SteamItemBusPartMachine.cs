#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

// Port of SteamItemBusPartMachine. LV (tier 1) item bus for steam multis -
// logically identical to ItemBusPartMachine at tier 1.
public class SteamItemBusPartMachine : ItemBusPartMachine
{
	protected override string Label => "Steam Item Bus";

	public SteamItemBusPartMachine() : base() { }

	public void Configure(IO io) => Configure(tier: 1, io: io);
}
