#nullable enable
using System.Numerics;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.Api.Capability;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.capability.IEnergyContainer.
// DO NOT modify behavior; mirror upstream changes only.
//
// Documented adaptations:
//   - Direction -> IODirection (Terraria 2D, 4 sides instead of Forge's 6).
//   - DEFAULT singleton dropped (Java idiom; C# callers can just check for
//     null).
//   - Java getter methods (`getEnergyStored()`, `getInputVoltage()`, ...) ->
//     C# properties (`EnergyStored`, `InputVoltage`, ...). Semantics identical;
//     compiled-out the same; convention matches the rest of our codebase.
//     The same translation applies to NotifiableEnergyContainer's impl.
public interface IEnergyContainer : IEnergyInfoProvider
{
	// This method is basically changeEnergy(long), but it also handles
	// amperes. This method should always be used when energy is passed
	// between blocks.
	//
	// voltage  - amount of energy packets (energy to add / input voltage)
	// amperage - packet size (energy to add / input amperage)
	// returns  - amount of used amperes. 0 if not accepted anything.
	long AcceptEnergyFromNetwork(IODirection side, long voltage, long amperage);

	// True if this container accepts energy from the given side.
	bool InputsEnergy(IODirection side);

	// True if this container can output energy to the given side.
	bool OutputsEnergy(IODirection side) => false;

	// Changes the amount stored. THIS SHOULD ONLY BE USED INTERNALLY (e.g.
	// draining while working or filling while generating). For transfer
	// between blocks use AcceptEnergyFromNetwork!
	//
	// differenceAmount - amount of energy to add (>0) or remove (<0)
	// returns          - amount of energy added or removed
	long ChangeEnergy(long differenceAmount);

	// Adds specified amount of energy to this energy container.
	long AddEnergy(long energyToAdd) => ChangeEnergy(energyToAdd);

	// Removes specified amount of energy from this energy container.
	long RemoveEnergy(long energyToRemove) => -ChangeEnergy(-energyToRemove);

	// Maximum amount of energy that can be inserted.
	long GetEnergyCanBeInserted() => EnergyCapacity - EnergyStored;

	// Amount of currently stored energy.
	long EnergyStored { get; }

	// Maximum amount of storable energy.
	long EnergyCapacity { get; }

	// Maximum amount of outputable energy packets per tick.
	long OutputAmperage => 0L;

	// Output energy packet size.
	long OutputVoltage => 0L;

	// Maximum amount of receivable energy packets per tick.
	long InputAmperage { get; }

	// Input energy packet size. Overflowing this value will explode the
	// machine.
	long InputVoltage { get; }

	// Input EU/s - used by display widgets. Upstream computes from
	// NotifiableEnergyContainer's rolling 20-tick stat.
	long IEnergyInfoProvider.GetInputPerSec() => 0L;

	// Output EU/s - symmetric.
	long IEnergyInfoProvider.GetOutputPerSec() => 0L;

	// Verbatim default from upstream - wraps stored/capacity as BigInteger
	// for display surfaces that need overflow-safe aggregation.
	IEnergyInfoProvider.EnergyInfo IEnergyInfoProvider.GetEnergyInfo() =>
		new(new BigInteger(EnergyCapacity), new BigInteger(EnergyStored));

	bool IEnergyInfoProvider.SupportsBigIntEnergyValues() => false;

	// === Terraria-side energy-net hooks =====================================
	// These three default-impl methods are how our 2D wire-net consumes a
	// producer/consumer container. Upstream doesn't need them - it talks to
	// IEnergyContainer through Forge's per-side capability lookup + each
	// producer's own `serverTick` push loop. Our pull-model wire-net needs:
	//
	//   - `EnergyFaceForCell(cx, cy)` - which logical face this footprint cell
	//     exposes. Defaults to `None` (sideless - every machine except the
	//     Transformer). The split-face Transformer overrides per row.
	//   - `GetPushAmperage()` - max amps the producer is willing to push this
	//     tick. Default = configured `OutputAmperage` clamped by what's
	//     actually backed by `EnergyStored`. EnergyBatteryTrait overrides to
	//     also clamp by non-empty-battery count.
	//   - `OnEnergyPushedToNetwork(amps, voltage)` - drain callback after the
	//     wire-net routes `amps x voltage` EU. Default = `ChangeEnergy(-amps*
	//     voltage)`. EnergyBatteryTrait overrides to distribute across batteries.

	IODirection EnergyFaceForCell(int cellX, int cellY) => IODirection.None;

	long GetPushAmperage()
	{
		long v = OutputVoltage;
		if (v <= 0) return 0;
		long byBuffer = EnergyStored / v;
		return System.Math.Min(OutputAmperage, byBuffer);
	}

	void OnEnergyPushedToNetwork(long amps, long voltage)
	{
		long drained = amps * voltage;
		if (drained <= 0) return;
		ChangeEnergy(-drained);
	}
}
