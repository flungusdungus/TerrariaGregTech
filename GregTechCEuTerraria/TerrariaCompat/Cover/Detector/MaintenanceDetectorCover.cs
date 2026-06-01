#nullable enable
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Machine.Feature.Multiblock;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Detector;

// Port of common.cover.detector.MaintenanceDetectorCover. Pulses when an
// adjacent (or host) maintenance hatch has problems.
//
// Adaptations: GTCapabilityHelper.getMaintenanceMachine -> GetMaintenanceTarget
// (CoverHolder first, then one footprint-cell outward via WorldCapability.
// Perimeter - same semantics adapted to our 2x2 + same-cell wiring).
// ConfigHolder.machines.enableMaintenance -> MaintenanceConfig.Enabled.
public class MaintenanceDetectorCover : DetectorCover
{
	public MaintenanceDetectorCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide) { }

	public override bool CanAttach() =>
		MaintenanceConfig.Enabled && base.CanAttach() && GetMaintenanceTarget() != null;

	protected override void Update()
	{
		if (CoverHolder.GetOffsetTimer() % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(20) != 0) return;

		var maintenance = GetMaintenanceTarget();
		if (maintenance == null) return;

		int signal = RedstoneSignalOutput;
		bool shouldSignal = IsInverted != maintenance.HasMaintenanceProblems();

		if (shouldSignal && signal != 15)
			SetRedstoneSignalOutput(15);
		else if (!shouldSignal && signal == 15)
			SetRedstoneSignalOutput(0);
	}

	// CoverHolder first, else the adjacent machine on AttachedSide.
	private IMaintenanceMachine? GetMaintenanceTarget()
	{
		if (CoverHolder is IMaintenanceMachine direct) return direct;
		if (CoverHolder is not MetaMachine machine) return null;
		var dir = WorldCapability.ToIODirection(AttachedSide);
		foreach (var (side, x, y) in WorldCapability.Perimeter(machine))
		{
			if (side != dir) continue;
			var neighbour = WorldCapability.Get<IMaintenanceMachine>(x, y);
			if (neighbour != null) return neighbour;
		}
		return null;
	}
}
