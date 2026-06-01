#nullable enable
namespace GregTechCEuTerraria.Api.Machine.Feature.Multiblock;

// Forward-decl for the Data Bank multiblock controller. When a `DataAccess
// HatchMachine` joins a controller that implements this interface, it
// validates data items against the broader Data Bank ruleset (allows
// recordable / writable data sticks too). When the controller is a regular
// Assembly Line, only finalised data items are accepted.
//
// Same pattern as `IMultiblockTankController` / `ICokeOvenController` - a
// concrete `DataBankMachine` (when ported) implements this. Equivalent to
// upstream's `controller instanceof DataBankMachine` class check.
public interface IDataBankController
{
}
