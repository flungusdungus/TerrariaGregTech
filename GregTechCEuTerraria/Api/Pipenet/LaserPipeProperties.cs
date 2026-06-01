#nullable enable
namespace GregTechCEuTerraria.Api.Pipenet;

// Port of com.gregtechceu.gtceu.common.pipelike.laser.LaserPipeProperties.
//
// Empty record - upstream's class has no fields. Single shared instance via
// `INSTANCE`. Kept as a class (not a struct) to match upstream's reference
// semantics + identity-equality.
public sealed class LaserPipeProperties
{
	public static readonly LaserPipeProperties INSTANCE = new();
	private LaserPipeProperties() { }
}
