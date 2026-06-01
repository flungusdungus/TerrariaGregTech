#nullable enable
namespace GregTechCEuTerraria.Api.Pipenet;

// Port of com.gregtechceu.gtceu.common.pipelike.optical.OpticalPipeProperties.
//
// Empty record - upstream's class has no fields. Single shared instance via
// `INSTANCE`. Kept as a class (not a struct) to match upstream's reference
// semantics + identity-equality. Mirror of LaserPipeProperties.
public sealed class OpticalPipeProperties
{
	public static readonly OpticalPipeProperties INSTANCE = new();
	private OpticalPipeProperties() { }
}
