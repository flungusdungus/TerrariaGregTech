#nullable enable
namespace GregTechCEuTerraria.Api.Data.Chemical.Material.Properties;

// Port of upstream com.gregtechceu.gtceu.api.data.chemical.material
// .properties.RotorProperty - per-material stats every turbine rotor made
// from this material inherits. Deserialized off the `rotor` block in
// Data/Materials/materials.json (emitted by the material registry dump).
//
// Damage + durability fields DROPPED - Terraria items don't carry durability
// and the rotor isn't a melee weapon in our port. RotorHolderPartMachine
// returns -1 for durability percent and `ApplyDamage` is a no-op.
public sealed class RotorProperty
{
	public int Power      { get; init; }
	public int Efficiency { get; init; }
}
