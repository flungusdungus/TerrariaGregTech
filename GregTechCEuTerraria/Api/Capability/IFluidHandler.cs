#nullable enable
using GregTechCEuTerraria.Api.Fluids;

namespace GregTechCEuTerraria.Api.Capability;

// Generic contract for anything that stores or transports fluid. Multi-tank
// by default - pipes have one tank per side, large multiblocks have tanks
// per ingredient, simple containers (Super Tank) have one tank.
//
// Mirrors the IItemHandler simulate-then-commit pattern: Fill/Drain accept
// a simulate flag so callers can pre-check "will this fit?" without mutating.
public interface IFluidHandler
{
	int TankCount { get; }

	// Returns the fluid currently in the tank. Empty if tank is empty.
	FluidStack GetTank(int tank);

	int GetCapacity(int tank);

	// Per-tank filter. Default accepts anything; override on tanks restricted
	// to one fluid type (e.g. a Steam Buffer tank only accepts steam).
	bool IsFluidValid(int tank, FluidStack fluid) => true;

	// Try to add fluid to any compatible tank. Returns amount actually accepted.
	// simulate=true means calculate without mutating. IO-direction gated - this
	// is the pipe / auto-transfer path. For UI / player interaction targeting a
	// specific tank, go through GetTankAccess instead.
	int Fill(FluidStack fluid, bool simulate);

	// Try to remove up to maxAmount of any fluid. Returns what was removed.
	// IO-direction gated (pipe path) - see Fill above.
	FluidStack Drain(int maxAmount, bool simulate);

	// Try to remove exactly the given stack (type-specific). Returns what
	// was actually removed (may be less than requested if not enough stored).
	FluidStack Drain(FluidStack fluidStack, bool simulate);

	// Returns the direction-free, single-tank handler backing the given tank
	// index - the player-interaction path. Mirrors upstream's
	// `NotifiableFluidTank.getStorages()[tank]`: UI fluid transfer (a bucket
	// click in a machine GUI, a world right-click) must reach the raw storage
	// so it bypasses IO direction - a player manually moving fluid is not pipe
	// auto-transfer. The whole-handler Fill/Drain stay IO-direction-gated for
	// pipes. Type filters live on the storage, so they are still enforced.
	//
	// Default `=> this`: a single-tank handler IS its own storage. Multi-tank
	// machines (boiler water+steam, recipe-machine import+export) override to
	// hand back the raw per-tank storage. Implement this - never re-derive
	// fill/drain direction logic per machine; that scatter is what let the
	// boiler dupe water.
	IFluidHandler GetTankAccess(int tank) => this;

	// Per-tank player-click capabilities - the canonical source-of-truth for
	// what a bucket / cell click on this tank is allowed to do, read by BOTH
	// the UI widget (tooltip + click-gate) and the FluidSlotAction server-side
	// gate. Mirrors upstream's per-`TankWidget` (allowClickDrained,
	// allowClickFilled) pair - except where upstream's MUI runs server-side
	// and reads the widget tree directly, we make the machine own the
	// declaration so the server can read it without seeing the layout.
	//
	//   AllowFill  - player may fill the tank from a filled bucket/cell.
	//   AllowDrain - player may drain the tank into an empty bucket/cell.
	//
	// Default (true, true) preserves current behavior for ad-hoc handlers
	// (super tank, drum). Recipe machines / boilers / coke oven override
	// per tank to match upstream's UI choices verbatim.
	(bool AllowFill, bool AllowDrain) GetTankClickCaps(int tank) => (true, true);
}

// Mirrors upstream's `IFluidHandlerModifiable extends IFluidHandler` - adds
// a `SetFluidInTank` that lets external code REPLACE a tank's contents
// directly (NBT load, swap-IO between sister hatches, test fixtures).
// Implementations that allow this expose it via the sub-interface; pipes /
// auto-transfer that only fill/drain use the base `IFluidHandler`.
public interface IFluidHandlerModifiable : IFluidHandler
{
	void SetFluidInTank(int tank, FluidStack stack);
}
