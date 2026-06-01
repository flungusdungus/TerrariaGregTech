#nullable enable
using GregTechCEuTerraria.Api.Capability;

namespace GregTechCEuTerraria.Api.Machine.Trait;

// Port of com.gregtechceu.gtceu.api.machine.trait.NotifiableLaserContainer.
//
// Energy container variant tagged as `ILaserContainer` - semantically
// identical to `NotifiableEnergyContainer` but routes through a separate
// pipe family (lasers, not standard cables).
//
// === Documented adaptations =================================================
//
//   - Same maxCapacity / maxInputVoltage / maxInputAmperage / maxOutputVoltage
//     / maxOutputAmperage tuple as the base class.
//   - `emitterContainer` / `receiverContainer` static factories - verbatim.
//   - `serverTick()` upstream walks neighbouring blocks looking for adjacent
//     `ILaserContainer` endpoints via `GTCapabilityHelper.getLaser` and
//     pushes EU to them. DROPPED - we don't have a laser-cable layer yet
//     (unported, same scope as `ActiveTransformer`'s
//     laser pipeline). The container still BUFFERS energy correctly; only
//     the per-tick neighbour-push transfer is dormant. When the laser-cable
//     layer lands, re-add the serverTick body - it's a small loop over
//     `WorldCapability.Perimeter`.
public class NotifiableLaserContainer : NotifiableEnergyContainer, ILaserContainer
{
	public static new readonly MachineTraitType<NotifiableLaserContainer> TYPE = new(allowMultipleInstances: true);
	public override MachineTraitType TraitType => TYPE;

	public NotifiableLaserContainer(long maxCapacity,
	                                long maxInputVoltage, long maxInputAmperage,
	                                long maxOutputVoltage, long maxOutputAmperage)
		: base(maxCapacity, maxInputVoltage, maxInputAmperage, maxOutputVoltage, maxOutputAmperage)
	{ }

	public static new NotifiableLaserContainer EmitterContainer(long maxCapacity,
	                                                            long maxOutputVoltage, long maxOutputAmperage)
		=> new(maxCapacity, 0L, 0L, maxOutputVoltage, maxOutputAmperage);

	public static new NotifiableLaserContainer ReceiverContainer(long maxCapacity,
	                                                             long maxInputVoltage, long maxInputAmperage)
		=> new(maxCapacity, maxInputVoltage, maxInputAmperage, 0L, 0L);

	// Override ServerTick to a NO-OP - laser pushes must NOT use the
	// inherited NotifiableEnergyContainer.ServerTick, which walks adjacent
	// MetaMachines and pushes through their NEC trait via
	// AcceptEnergyFromNetwork. That path bypasses tier-matching entirely:
	// an IV laser hatch adjacent to an EV energy hatch would push 8192V at
	// the EV hatch's NEC (InputVoltage 2048V) every single tick, triggering
	// the over-voltage explosion. Upstream avoids this because their NEC
	// uses a different capability token (CAPABILITY_ENERGY vs CAPABILITY_
	// LASER); our flat trait-walk doesn't distinguish.
	//
	// The laser hatch's actual push is in LaserHatchPartMachine.OnTick,
	// which walks PerimeterCells looking for ILaserContainers (laser pipes
	// or direct-adjacent laser hatches) - the dedicated, tier-respecting
	// route. ServerTick stays no-op so the laser buffer only emits through
	// the laser-pipe path.
	protected override void ServerTick() { }
}
