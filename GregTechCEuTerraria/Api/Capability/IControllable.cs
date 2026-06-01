#nullable enable
namespace GregTechCEuTerraria.Api.Capability;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.capability.IControllable.
// DO NOT modify behavior; mirror upstream changes only.
//
// Anything whose work can be paused / resumed externally. The base of
// IWorkable. RecipeLogic implements both - `isWorkingEnabled` gates the
// state machine; `setSuspendAfterFinish` lets a running cycle complete
// before transitioning to SUSPEND.
//
// Documented adaptation:
//   - WORKING_ENABLED_PROPERTY (Forge BlockState property) dropped - we
//     don't have block-state properties; the flag is just a TileEntity
//     field synced via MachineStateSyncPacket.
public interface IControllable
{
	bool IsWorkingEnabled();
	void SetWorkingEnabled(bool isWorkingAllowed);

	void SetSuspendAfterFinish(bool suspendAfterFinish) { }
	bool IsSuspendAfterFinish() => false;
}
