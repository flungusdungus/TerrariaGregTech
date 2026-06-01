#nullable enable
namespace GregTechCEuTerraria.Api.Capability;

// LOCKED - verbatim port of com.gregtechceu.gtceu.api.capability.IWorkable.
// DO NOT modify behavior; mirror upstream changes only.
//
// Surface for machines that have progress and can work. Extends
// IControllable for the pause/resume contract. UI widgets bind to
// GetProgress() / GetMaxProgress() for progress arrows, IsActive() for
// active-state animations.
//
// Documented adaptation:
//   - ACTIVE_PROPERTY (Forge BlockState property) dropped - flag synced
//     via MachineStateSyncPacket field.
public interface IWorkable : IControllable
{
	int GetProgress();
	int GetMaxProgress();
	bool IsActive();
}
