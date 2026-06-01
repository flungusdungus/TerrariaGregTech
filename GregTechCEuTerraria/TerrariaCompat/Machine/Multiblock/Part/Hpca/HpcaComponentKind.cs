#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part.Hpca;

// Discriminator for the collapsed HPCAComponentPartMachine entity. One entity
// class backs all six upstream HPCA grid components (each its own machine id,
// all tier ZPM); the MachineDefinition row carries the kind and the entity's
// OnDefinitionBound builds the matching trait. Mirrors the family-entity
// collapse used everywhere else in this codebase to stay under the tML
// 256-tile-entity-type cap.
public enum HpcaComponentKind : byte
{
	Empty               = 0,  // HPCAEmptyPartMachine          - trait(0,0,false,false)
	Computation         = 1,  // HPCAComputationPartMachine     - upkeep EV, max LuV, cwu 4,  cool 2
	AdvancedComputation = 2,  // HPCAComputationPartMachine adv - upkeep IV, max ZPM, cwu 16, cool 4
	HeatSink            = 3,  // HPCACoolerPartMachine passive  - cooling 1, no coolant
	ActiveCooler        = 4,  // HPCACoolerPartMachine active   - upkeep IV, cooling 2, coolant 8 mB/t
	Bridge              = 5,  // HPCABridgePartMachine          - upkeep/max IV, allowBridging
}
