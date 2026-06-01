#nullable enable
namespace GregTechCEuTerraria.Api.Fluids.Attribute;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.fluids.attribute.FluidAttributes.
// DO NOT modify behavior; mirror upstream changes only.
//
// Static holder of well-known FluidAttribute instances. New attribute kinds
// land here as new static readonly fields, never as subclasses (FluidAttribute
// is concrete and final - identity is by Id, not by type).
public static class FluidAttributes
{
	// Acidic fluid - eats containers that aren't acid-resistant.
	// Cell items check this in their CanFill predicate.
	public static readonly FluidAttribute ACID = new(
		id: "acid",
		fluidTooltip:     list => list("Acidic"),
		containerTooltip: list => list("Acid-proof"));
}
