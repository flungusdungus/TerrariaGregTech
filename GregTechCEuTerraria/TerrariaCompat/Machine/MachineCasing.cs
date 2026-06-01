#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Machine hull casing kind. Pure-data mirror of MachineRenderer.Casing so that
// MachineDefinition carries no rendering / tML dependency (keeps the whole
// definition layer unit-testable). MachineRenderer maps this to its own enum
// at draw time.
public enum MachineCasing
{
	Voltage,        // standard per-tier voltage casing
	BrickedBronze,  // low-pressure steam machines - upstream casings/steam/bricked_bronze
	BrickedSteel,   // high-pressure steam machines - upstream casings/steam/bricked_steel
	CokeBricks,     // coke_oven multi + its hatch (machine_coke_bricks)
	Firebricks,     // primitive_blast_furnace multi (machine_primitive_bricks)
	PumpDeck,       // primitive_pump multi + its pump_hatch (pump_deck/top)
	None,           // no casing (solar panel)
}
