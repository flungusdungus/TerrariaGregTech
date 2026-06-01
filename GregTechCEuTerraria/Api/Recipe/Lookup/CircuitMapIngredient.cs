#nullable enable
namespace GregTechCEuTerraria.Api.Recipe.Lookup;

// ADAPTED - no direct upstream counterpart.
//
// Trie node-key for a programmed-circuit configuration value.
//
// Documented adaptation: upstream's IntCircuitIngredient IS a real
// ProgrammedCircuit item (with the config in NBT), so it flows through the
// trie as an ordinary ItemStackMapIngredient. Our IntCircuitIngredient has no
// backing item - it matches the machine's circuit-selector integer (see
// IntCircuitIngredient.cs). So the trie carries circuit configs as their own
// node-key type: a recipe's circuit ingredient compiles to one of these, and
// the query side always contributes the machine's current CircuitSelector
// (RecipeLookupCompiler.CompileQuery).
public sealed class CircuitMapIngredient : AbstractMapIngredient
{
	// IntCircuitIngredient.Configuration / the machine's circuit-selector value.
	public readonly int Config;

	public CircuitMapIngredient(int config)
	{
		Config = config;
	}

	protected override int Hash() => Config;

	protected override bool EqualsSameClass(AbstractMapIngredient other) =>
		// EqualsSameClass is only called when `other` is the same class.
		Config == ((CircuitMapIngredient)other).Config;

	public override string ToString() => $"CircuitMapIngredient{{config={Config}}}";
}
