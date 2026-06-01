#nullable enable
using System.Collections.Generic;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

// Shared power diagnostic for manual-energy research multis (HPCA / DataBank
// / NetworkSwitch). They drive status via per-tick ConsumeEnergy (not
// RecipeLogic), so brownout only flickers IsActive - without this, an
// underpowered controller reads "Idling" / "Running Perfectly" off a briefly
// charged buffer. Recipe-driven multis get this for free via RecipeStatusText.
public interface IPowerDiagnostics
{
	long PowerUpkeep { get; }
	long PowerCapacity { get; }
	long PowerMaxInput { get; }  // capability = inputVoltage x inputAmperage
	long PowerStored { get; }
}

public static class ResearchPowerDiagnostics
{
	// MUST use CAPABILITY (V x A), not GetInputPerSec throughput: throughput
	// equals consumption when the buffer is full, so any check flickers at the
	// boundary even with huge headroom.
	public static bool IsSufficient(IPowerDiagnostics m)
	{
		long up = m.PowerUpkeep;
		if (up <= 0) return true;
		if (m.PowerCapacity == 0) return false;
		if (m.PowerMaxInput < up) return false;
		return m.PowerStored >= up;
	}

	// True if sustainable (no append); else appends actionable line.
	// Distinguishes: no hatch / capability too low / capable but unwired.
	public static bool Append(IPowerDiagnostics m, List<string> lines)
	{
		long up = m.PowerUpkeep;
		if (up <= 0) return true;  // nothing to power yet

		if (m.PowerCapacity == 0)
		{
			lines.Add("[c/FF5555:No Energy Input Hatch - add one to the structure]");
			return false;
		}
		if (m.PowerMaxInput < up)
		{
			lines.Add($"[c/FF5555:Energy Hatch input too low: {m.PowerMaxInput:N0} EU/t max < {up:N0} EU/t upkeep]");
			lines.Add("[c/FFAA44:Use a higher-tier or higher-amperage Energy Hatch (or add more)]");
			return false;
		}
		if (m.PowerStored < up)
		{
			// Capable hatch but buffer empty - unwired / weak upstream source.
			lines.Add($"[c/FF5555:Not enough power flowing in - upkeep {up:N0} EU/t]");
			lines.Add("[c/FFAA44:Wire a stronger generator/battery into the Energy Hatch]");
			return false;
		}
		return true;
	}
}
