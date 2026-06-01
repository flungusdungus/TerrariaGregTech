#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Machine.Multiblock;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.Common.Machine.Trait;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

// Port of CleaningMaintenanceHatchPartMachine. AutoMaintenanceHatch +
// cleanroom provider (CLEANROOM/STERILE_CLEANROOM); satisfies a controller's
// CleanroomReceiverTrait. Tier per type: CLEANROOM=UV, STERILE=UHV.
public class CleaningMaintenanceHatchPartMachine : AutoMaintenanceHatchPartMachine
{
	protected override string Label => "Cleaning Maintenance Hatch";

	public CleanroomType CleanroomType { get; private set; } = CleanroomType.CLEANROOM;

	// Active-when-bound; toggled by Added/RemovedFromController.
	protected CleanroomProviderTrait? CleanroomProvider;

	public CleaningMaintenanceHatchPartMachine() : base() { }

	public void Configure(CleanroomType cleanroomType)
	{
		CleanroomType = cleanroomType;
		Tier = cleanroomType == CleanroomType.CLEANROOM
			? (int)VoltageTier.UV
			: (int)VoltageTier.UHV;
		EnsureCleanroomProvider();
	}

	protected override void OnDefinitionBound()
	{
		// Skip AutoMaintenance.OnDefinitionBound (sets Tier=HV) so the cleanroom tier survives.
		if (Definition == null) return;
		var ct = Definition.PartCleanroomType ?? CleanroomType.CLEANROOM;
		Configure(ct);
	}

	private void EnsureCleanroomProvider()
	{
		if (CleanroomProvider != null)
		{
			CleanroomProvider.ProvidedTypes = new HashSet<CleanroomType> { CleanroomType };
			return;
		}
		CleanroomProvider = new CleanroomProviderTrait(new HashSet<CleanroomType> { CleanroomType });
		Traits.Attach(CleanroomProvider);
		// Not persistent - re-derived from definition on reload.
	}

	public override void AddedToController(MultiblockControllerMachine controller)
	{
		base.AddedToController(controller);
		EnsureCleanroomProvider();
		// One receiver per controller; reuse a sibling's if already installed.
		var receiver = controller.Traits.GetTrait(CleanroomReceiverTrait.TYPE);
		if (receiver == null)
		{
			receiver = new CleanroomReceiverTrait();
			controller.Traits.Attach(receiver);
		}
		receiver.CleanroomProvider = CleanroomProvider;
		CleanroomProvider!.IsActive = true;
	}

	public override void RemovedFromController(MultiblockControllerMachine controller)
	{
		base.RemovedFromController(controller);
		var receiver = controller.Traits.GetTrait(CleanroomReceiverTrait.TYPE);
		if (receiver != null && ReferenceEquals(receiver.CleanroomProvider, CleanroomProvider))
			receiver.RemoveCleanroom();
		if (CleanroomProvider != null) CleanroomProvider.IsActive = false;
	}

	public override void AppendTooltip(System.Collections.Generic.List<string> lines)
	{
		base.AppendTooltip(lines);
		lines.Add($"Provides: {CleanroomType}");
	}
}
