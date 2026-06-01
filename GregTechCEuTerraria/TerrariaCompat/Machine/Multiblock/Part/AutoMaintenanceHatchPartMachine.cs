#nullable enable
using GregTechCEuTerraria.Api.Machine.Feature.Multiblock;
using GregTechCEuTerraria.Common.Energy;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

// Port of AutoMaintenanceHatchPartMachine. HV, never has problems, no tape.
// Cleaning variant subclasses for cleanroom provision.
public class AutoMaintenanceHatchPartMachine : TieredPartMachine, IMaintenanceMachine
{
	protected override string Label => "Full-Auto Maintenance Hatch";

	public AutoMaintenanceHatchPartMachine() : base() { }

	public void Configure() => Tier = (int)VoltageTier.HV;

	protected override void OnDefinitionBound()
	{
		base.OnDefinitionBound();
		if (Definition == null) return;
		Configure();
	}

	public void SetTaped(bool ignored)  { }
	public bool IsTaped()               => false;
	public bool IsFullAuto()            => true;
	public byte StartProblems()         => IMaintenanceMachine.NO_PROBLEMS;
	public byte GetMaintenanceProblems()=> IMaintenanceMachine.NO_PROBLEMS;
	public void SetMaintenanceProblems(byte problems) { }
	public int  GetTimeActive()         => 0;
	public void SetTimeActive(int time) { }

	public override void AppendTooltip(System.Collections.Generic.List<string> lines)
	{
		base.AppendTooltip(lines);
		lines.Add("[c/55FF55:Self-maintaining]");
	}
}
