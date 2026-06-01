#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.Api.Misc;

// Port of com.gregtechceu.gtceu.api.misc.EnergyContainerList.
//
// Wraps multiple `IEnergyContainer`s as one (the multi controller's view over
// all bound energy hatches). Aggregates input/output voltage + amperage with
// upstream's compaction algorithm (sum-voltage-times-amperage, then divide
// down to amperage < 4 where possible - `WorkableElectricMultiblockMachine.
// getMaxVoltage` reads `highestInputVoltage` + `numHighestInputContainers`
// to grant a tier bonus when multiple top-tier hatches are present).
//
// Documented adaptations:
//   - `Direction` (Forge 6-face) -> `IODirection` (our 4-state enum). The
//     dispatch methods upstream are called with `null` Direction - we pass
//     `IODirection.None` to mean "no specific side".
//   - `getInputPerSec`/`getOutputPerSec` come from `IEnergyInfoProvider`
//     upstream; our `IEnergyContainer` inherits the same surface.
public sealed class EnergyContainerList : IEnergyContainer
{
	private readonly List<IEnergyContainer> _containers;

	public long InputVoltage  { get; }
	public long OutputVoltage { get; }

	// Always < 4. A list with amps > 4 is compacted into more voltage at fewer amps.
	public long InputAmperage  { get; }
	public long OutputAmperage { get; }

	// The highest single energy container's input voltage in the list.
	public long HighestInputVoltage { get; }

	// The number of energy containers at the highest input voltage in the list.
	public int NumHighestInputContainers { get; }

	public EnergyContainerList(List<IEnergyContainer> containers)
	{
		_containers = containers;
		long totalInputVoltage = 0;
		long totalOutputVoltage = 0;
		long inputAmperage = 0;
		long outputAmperage = 0;
		long highestInputVoltage = 0;
		int numHighestInputContainers = 0;
		foreach (var c in containers)
		{
			totalInputVoltage  += c.InputVoltage  * c.InputAmperage;
			totalOutputVoltage += c.OutputVoltage * c.OutputAmperage;
			inputAmperage  += c.InputAmperage;
			outputAmperage += c.OutputAmperage;
			if (c.InputVoltage > highestInputVoltage)
				highestInputVoltage = c.InputVoltage;
		}
		foreach (var c in containers)
		{
			if (c.InputVoltage == highestInputVoltage)
				numHighestInputContainers++;
		}

		// Empty-list (or all-zero-amperage) short-circuit. Upstream's
		// EnergyStack constraint is `@Range(from=1, ...)` - a Java annotation
		// that's compile-time only - so upstream's EnergyContainerList
		// silently builds (0,0). Our port enforces the constraint at
		// runtime; bypass the EnergyStack construction when there's no
		// containers to aggregate (cleanroom calls GetEnergyContainer()
		// during OnStructureFormed BEFORE the input hatches are walked).
		if (inputAmperage == 0)
		{
			InputVoltage = 0; InputAmperage = 0;
		}
		else
		{
			var inStack  = CalculateVoltageAmperage(totalInputVoltage,  inputAmperage);
			InputVoltage = inStack.Voltage; InputAmperage = inStack.Amperage;
		}
		if (outputAmperage == 0)
		{
			OutputVoltage = 0; OutputAmperage = 0;
		}
		else
		{
			var outStack = CalculateVoltageAmperage(totalOutputVoltage, outputAmperage);
			OutputVoltage = outStack.Voltage; OutputAmperage = outStack.Amperage;
		}

		HighestInputVoltage       = highestInputVoltage;
		NumHighestInputContainers = numHighestInputContainers;
	}

	// Computes the correct max voltage and amperage values. Verbatim from
	// upstream EnergyContainerList.calculateVoltageAmperage.
	//
	//   amperage > 4              : compact down to <=4 by dividing
	//   amperage in {3, 5, 6, ...}   : reduce to 1A at sum-of-volt*amp voltage
	//   amperage power-of-4       : reduce to 1A
	//   amperage divisible by 4   : reduce to <4 by dividing
	//   amperage == 2             : reduce to 1A at half voltage
	public static EnergyStack CalculateVoltageAmperage(long voltage, long amperage)
	{
		if (voltage > 1 && amperage > 1)
		{
			if (HasPrimeFactorGreaterThanTwo(amperage))
			{
				// 3A, 5A, 6A, ...
				amperage = 1;
			}
			else if (IsPowerOfFour(amperage))
			{
				// 4A, 16A, ...
				amperage = 1;
			}
			else if (amperage % 4 == 0)
			{
				// 8A, 32A, ...  reduced to <4 amps and equivalent voltage
				while (amperage > 4)
					amperage /= 4;
				voltage /= amperage;
			}
			else if (amperage == 2)
			{
				voltage /= amperage;
			}
			else
			{
				// fallback case, that should never be hit
				// forced to 1A to prevent excess power draw/output if something falls through
				amperage = 1;
			}
		}
		return new EnergyStack(voltage, amperage);
	}

	private static bool HasPrimeFactorGreaterThanTwo(long l)
	{
		int i = 2;
		long max = l / 2;
		while (i <= max)
		{
			if (l % i == 0)
			{
				if (i > 2) return true;
				l /= i;
			}
			else
			{
				i++;
			}
		}
		return false;
	}

	// Power of 4 (excluding 1). Verbatim bit twiddle from upstream.
	private static bool IsPowerOfFour(long l)
	{
		if (l == 0) return false;
		if ((l & (l - 1)) != 0) return false;
		return (l & 0x55555555) != 0;
	}

	// === IEnergyContainer ===================================================

	public long AcceptEnergyFromNetwork(IODirection side, long voltage, long amperage)
	{
		long amperesUsed = 0L;
		foreach (var c in _containers)
		{
			amperesUsed += c.AcceptEnergyFromNetwork(IODirection.None, voltage, amperage);
			if (amperesUsed >= amperage)
				return amperesUsed;
		}
		return amperesUsed;
	}

	public long ChangeEnergy(long energyToAdd)
	{
		long energyAdded = 0L;
		foreach (var c in _containers)
		{
			energyAdded += c.ChangeEnergy(energyToAdd - energyAdded);
			if (energyAdded == energyToAdd)
				return energyAdded;
		}
		return energyAdded;
	}

	public long EnergyStored
	{
		get
		{
			long sum = 0L;
			foreach (var c in _containers) sum += c.EnergyStored;
			return sum;
		}
		set
		{
			// Backwards-set isn't meaningful for a list; we drive energy via
			// ChangeEnergy. Setter present to satisfy the IEnergyContainer
			// interface contract.
			long target = value;
			long current = EnergyStored;
			ChangeEnergy(target - current);
		}
	}

	public long EnergyCapacity
	{
		get
		{
			long sum = 0L;
			foreach (var c in _containers) sum += c.EnergyCapacity;
			return sum;
		}
	}

	public bool InputsEnergy(IODirection side)  => true;
	public bool OutputsEnergy(IODirection side) => true;

	public long GetInputPerSec()
	{
		long sum = 0L;
		foreach (var c in _containers) sum += c.GetInputPerSec();
		return sum;
	}

	public long GetOutputPerSec()
	{
		long sum = 0L;
		foreach (var c in _containers) sum += c.GetOutputPerSec();
		return sum;
	}

	public override string ToString() =>
		$"EnergyContainerList{{containers={_containers.Count}, stored={EnergyStored}, capacity={EnergyCapacity}, " +
		$"inV={InputVoltage}, inA={InputAmperage}, outV={OutputVoltage}, outA={OutputAmperage}}}";
}
