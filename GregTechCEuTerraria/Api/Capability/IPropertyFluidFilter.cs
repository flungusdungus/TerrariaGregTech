#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Fluids.Attribute;

namespace GregTechCEuTerraria.Api.Capability;

// Port of com.gregtechceu.gtceu.api.capability.IPropertyFluidFilter.
//
// A predicate over a FluidStack - true if the implementing container / pipe
// may hold the fluid. Implemented by FluidPipeProperties; DrumMachine installs
// it as its tank filter.
//
// Documented adaptation: upstream's `test` has a branch for plain (non-GregTech)
// vanilla fluids - `fluid instanceof IAttributedFluid`. Every FluidType in this
// port is "attributed" (carries State + Temperature + Attributes), so that
// branch never applies and is omitted; the attributed path is verbatim.
public interface IPropertyFluidFilter
{
	// FluidConstants.CRYOGENIC_FLUID_THRESHOLD.
	const int CryogenicFluidThreshold = 120;

	// Verbatim port of IPropertyFluidFilter.test.
	bool Test(FluidStack stack)
	{
		if (stack.IsEmpty || stack.Type is null) return true;
		FluidType fluid = stack.Type;

		if (fluid.Temperature < CryogenicFluidThreshold && !CryoProof) return false;

		FluidState state = fluid.State;
		if (!CanContain(state)) return false;
		foreach (FluidAttribute attribute in fluid.Attributes)
			if (!CanContain(attribute))
				return false;

		// Plasma ignores temperature requirements.
		if (state == FluidState.PLASMA) return true;

		return fluid.Temperature <= MaxFluidTemperature;
	}

	// True if the state can be contained.
	bool CanContain(FluidState state);

	// True if the attribute can be contained.
	bool CanContain(FluidAttribute attribute);

	// Set whether an attribute can be contained.
	void SetCanContain(FluidAttribute attribute, bool canContain);

	IReadOnlyCollection<FluidAttribute> ContainedAttributes { get; }

	// Always checked, regardless of the contained fluid - the maximum allowed
	// fluid temperature.
	int MaxFluidTemperature { get; }

	// Whether this filter allows gases.
	bool GasProof { get; }

	// Whether this filter allows cryogenic fluids.
	bool CryoProof { get; }

	// Whether this filter allows plasmas.
	bool PlasmaProof { get; }
}
