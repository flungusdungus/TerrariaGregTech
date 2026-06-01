#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Fluids.Attribute;

namespace GregTechCEuTerraria.Api.Data.Chemical.Material.Properties;

// Port of com.gregtechceu.gtceu.api.data.chemical.material.properties.FluidPipeProperties.
//
// A material's fluid-containment property - what a pipe (or a drum) made of the
// material may hold: a maximum fluid temperature, plus gas / cryo / plasma /
// acid containment. Implements IPropertyFluidFilter - the filter DrumMachine
// installs on its tank, so a wooden drum genuinely cannot hold lava or a gas.
//
// Deserialized straight off Data/Materials/materials.json's `fluidPipe` block
// (DataGenerators dumps every field). Throughput / Channels are carried for
// parity but unused until pipe routing is ported.
//
// Documented adaptation: upstream stores containment in an
// Object2BooleanMap<FluidAttribute>, seeded by the ctor from the `acidProof`
// arg. We keep the map verbatim; `AcidProof` is a JSON-init shim whose setter
// seeds the map (the JSON carries the flat `acidProof` bool, not a map).
public sealed class FluidPipeProperties : IPropertyFluidFilter
{
	// The maximum number of channels any fluid pipe can have.
	public const int MAX_PIPE_CHANNELS = 9;

	public int  Throughput          { get; init; }
	public int  Channels            { get; init; }
	public int  MaxFluidTemperature { get; init; }
	public bool GasProof            { get; init; }
	public bool CryoProof           { get; init; }
	public bool PlasmaProof         { get; init; }

	private readonly Dictionary<FluidAttribute, bool> _containmentPredicate = new();

	// Upstream's ctor does `if (acidProof) setCanContain(FluidAttributes.ACID,
	// true)`. The dump carries the flat `acidProof` bool, so this init-setter
	// shim replays that into the containment map at deserialization.
	public bool AcidProof
	{
		get => CanContain(FluidAttributes.ACID);
		init { if (value) SetCanContain(FluidAttributes.ACID, true); }
	}

	// FluidPipeProperties.canContain(FluidState).
	public bool CanContain(FluidState state) => state switch
	{
		FluidState.LIQUID => true,
		FluidState.GAS    => GasProof,
		FluidState.PLASMA => PlasmaProof,
		_                 => true,
	};

	// FluidPipeProperties.canContain(FluidAttribute) - the containmentPredicate
	// map defaults to false for any attribute never set.
	public bool CanContain(FluidAttribute attribute) =>
		_containmentPredicate.TryGetValue(attribute, out bool v) && v;

	public void SetCanContain(FluidAttribute attribute, bool canContain) =>
		_containmentPredicate[attribute] = canContain;

	public IReadOnlyCollection<FluidAttribute> ContainedAttributes => _containmentPredicate.Keys;
}
