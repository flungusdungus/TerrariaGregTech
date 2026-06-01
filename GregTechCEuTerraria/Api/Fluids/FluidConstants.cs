#nullable enable
namespace GregTechCEuTerraria.Api.Fluids;

// LOCKED - verbatim port of com.gregtechceu.gtceu.api.fluids.FluidConstants.
// DO NOT modify values; mirror upstream changes only.
//
// Inference constants for FluidBuilder.Determine* - the temperature / density /
// viscosity defaults a fluid falls back to when its FluidBuilder leaves a
// value at INFER.
public static class FluidConstants
{
	public const int ROOM_TEMPERATURE = 293;

	// Base liquid temperature for primarily solid materials.
	public const int SOLID_LIQUID_TEMPERATURE = 1200;

	// Base plasma temperature, and offset for materials with blast temperatures
	// when as plasma.
	public const int BASE_PLASMA_TEMPERATURE = 10000;

	// Offset for materials with blast temperatures, when as liquid.
	public const int LIQUID_TEMPERATURE_OFFSET = 0;

	// Offset for materials with blast temperatures, when as gases.
	public const int GAS_TEMPERATURE_OFFSET = 100;

	public const int DEFAULT_LIQUID_DENSITY = 1000;
	public const int DEFAULT_GAS_DENSITY = -100;
	public const int DEFAULT_PLASMA_DENSITY = -100000;
	public const int DEFAULT_MOLTEN_DENSITY = 1500;

	// Viscosity for sticky materials.
	public const int STICKY_LIQUID_VISCOSITY = 2000;

	public const int DEFAULT_LIQUID_VISCOSITY = 1000;
	public const int DEFAULT_GAS_VISCOSITY = 200;
	public const int DEFAULT_PLASMA_VISCOSITY = 10;
	public const int DEFAULT_MOLTEN_VISCOSITY = 2000;

	// Threshold for fluids to be considered cryogenic. Temperatures lower than
	// this are considered cryogenic.
	public const int CRYOGENIC_FLUID_THRESHOLD = 120;
}
