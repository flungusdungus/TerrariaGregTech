#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.Common.Energy;

// Tiny read-only `IEnergyContainer` snapshot wrapper used by the multiblock
// display pipeline (`WorkableElectricMultiblockMachine.GetDisplayEnergy
// Container`). The live `EnergyContainerList` aggregator walks the
// controller's bound `_parts` list - empty on MP clients (see comment in
// `MultiblockControllerMachine.OnStructureFormed` line 222-227: clients don't
// run `AsyncCheckPattern`, the server broadcasts the formed-edge instead).
// Without a snapshot, `MultiblockDisplayText.AddEnergyUsageLine` would gate
// out on `EnergyCapacity > 0` and the panel would silently drop the
// "Max Energy" line on every client.
//
// Carries only the values the builder reads (capacity / in+out voltage /
// in amperage / stored). Mutation methods are no-ops - this is a display-side
// container only.
internal sealed class MultiblockDisplayEnergyContainer : IEnergyContainer
{
	public long EnergyStored   { get; }
	public long EnergyCapacity { get; }
	public long InputVoltage   { get; }
	public long OutputVoltage  { get; }
	public long InputAmperage  { get; }
	public long OutputAmperage { get; }

	public MultiblockDisplayEnergyContainer(long stored, long capacity,
		long inputVoltage, long outputVoltage, long inputAmperage, long outputAmperage)
	{
		EnergyStored   = stored;
		EnergyCapacity = capacity;
		InputVoltage   = inputVoltage;
		OutputVoltage  = outputVoltage;
		InputAmperage  = inputAmperage;
		OutputAmperage = outputAmperage;
	}

	// Mutation surface - no-ops. Display-only.
	public long AcceptEnergyFromNetwork(IODirection side, long voltage, long amperage) => 0;
	public bool InputsEnergy(IODirection side) => InputVoltage > 0;
	public long ChangeEnergy(long differenceAmount) => 0;
}
