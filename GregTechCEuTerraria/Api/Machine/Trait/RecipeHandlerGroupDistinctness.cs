#nullable enable
namespace GregTechCEuTerraria.Api.Machine.Trait;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.machine.trait.RecipeHandlerGroupDistinctness.
//
// Group sentinel for the two distinctness modes. Multiblocks with multiple
// item buses use BUS_DISTINCT to force per-bus recipe matching. Capabilities
// that should bypass distinct grouping declare BYPASS_DISTINCT.
public sealed class RecipeHandlerGroupDistinctness : RecipeHandlerGroup
{
	public static readonly RecipeHandlerGroupDistinctness BUS_DISTINCT    = new("BUS_DISTINCT");
	public static readonly RecipeHandlerGroupDistinctness BYPASS_DISTINCT = new("BYPASS_DISTINCT");

	public string Name { get; }
	private RecipeHandlerGroupDistinctness(string name) { Name = name; }

	// Singleton identity - only the two static instances exist.
	public override bool Equals(object? obj) => ReferenceEquals(this, obj);
	public override int GetHashCode() => Name.GetHashCode();
	public override string ToString() => Name;
}
