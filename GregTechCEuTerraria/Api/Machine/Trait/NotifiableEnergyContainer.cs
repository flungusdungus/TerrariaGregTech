#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine;
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Recipes;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.Api.Capability.Recipe;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Machine.Trait;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.machine.trait.NotifiableEnergyContainer.
// DO NOT modify behavior; mirror upstream changes only.
//
// Adaptations: Direction -> IODirection (4 sides, no Up/Down); neighbor lookup
// resolves trait->trait via the neighbor MetaMachine's holder; the output/update
// TickableSubscription opt (subscribe only when stored >= outputVoltage) dropped -
// ServerTick is unconditional; markClientSyncFieldDirty -> MachineStateSyncPacket
// (Save/Load); getOffsetTimer() -> Main.GameUpdateCount; over-voltage explosion
// goes through the ported EnvironmentalExplosionTrait lookup; Forge Energy compat
// dropped (no FE in Terraria).
public class NotifiableEnergyContainer
	: NotifiableRecipeHandlerTrait<Api.Recipe.Ingredient.EnergyStack>, IEnergyContainer
{
	public static readonly MachineTraitType<NotifiableEnergyContainer> TYPE = new(allowMultipleInstances: true);

	public override MachineTraitType TraitType => TYPE;

	public IO HandlerIO { get; protected set; }

	// Persisted + synced. Setter mirrors upstream's setEnergyStored:
	// recompute stats, notify listeners. Direct field access only inside
	// SetEnergyStored / Load - matches upstream's `this.energyStored =`
	// assignments which bypass the setter inside specific methods.
	// Underscore-field for the base trait. Subclasses (EnergyBatteryTrait) that
	// derive EnergyStored from machine inventory override the public virtual
	// without touching this field.
	protected long _energyStored;
	public virtual long EnergyStored => _energyStored;

	// Internal back-field for the EnergyCapacity property - `protected set` so
	// subclasses can mutate during ResetBasicInfo; getter is virtual so trait
	// overrides (battery-buffer's sum-over-batteries) can supplant it.
	protected long _energyCapacity;
	public virtual long EnergyCapacity { get => _energyCapacity; protected set => _energyCapacity = value; }
	public long InputVoltage   { get; private set; }
	public long InputAmperage  { get; private set; }
	public long OutputVoltage  { get; private set; }
	public long OutputAmperage { get; private set; }

	// Optional per-side I/O gates (e.g. a cover blocks a side). Null = always
	// allow. Matches upstream Predicate<Direction> fields.
	public Predicate<IODirection>? SideInputCondition  { get; set; }
	public Predicate<IODirection>? SideOutputCondition { get; set; }

	// Per-tick amp counter for input throttling. Resets when the world tick
	// advances past `_lastTimeStamp`.
	protected long _amps;
	protected long _lastTimeStamp;

	// Subscription handles for the two tick callbacks. `outputSubs` is
	// conditionally (re)subscribed by CheckOutputSubscription whenever
	// EnergyStored crosses the OutputVoltage threshold. `updateSubs` is
	// subscribed once in OnMachineLoad and runs every tick to rotate stats.
	protected TickableSubscription? _outputSubs;
	protected TickableSubscription? _updateSubs;

	// 20-tick rolling input/output stats. Upstream uses 20 because MC is 20
	// TPS; Terraria is 60 TPS, but we preserve the 20-tick window verbatim -
	// the stat is "EU per 1/3 second" rather than "EU per second", and any
	// downstream code that reads it (display, balancing math) carries the
	// same scale as upstream. Adjusting the window would diverge from
	// upstream's numbers.
	protected long _lastEnergyInputPerSec  = 0;
	protected long _lastEnergyOutputPerSec = 0;
	protected long _energyInputPerSec      = 0;
	protected long _energyOutputPerSec     = 0;

	public NotifiableEnergyContainer(long maxCapacity,
	                                 long maxInputVoltage, long maxInputAmperage,
	                                 long maxOutputVoltage, long maxOutputAmperage)
	{
		_lastTimeStamp  = long.MinValue;
		EnergyCapacity  = maxCapacity;
		InputVoltage    = maxInputVoltage;
		InputAmperage   = maxInputAmperage;
		OutputVoltage   = maxOutputVoltage;
		OutputAmperage  = maxOutputAmperage;
		bool isIn  = (InputVoltage  != 0 && InputAmperage  != 0);
		bool isOut = (OutputVoltage != 0 && OutputAmperage != 0);
		HandlerIO = (isIn && isOut) ? IO.BOTH : isIn ? IO.IN : isOut ? IO.OUT : IO.NONE;
	}

	public static NotifiableEnergyContainer EmitterContainer(long maxCapacity,
	                                                          long maxOutputVoltage, long maxOutputAmperage)
		=> new(maxCapacity, 0L, 0L, maxOutputVoltage, maxOutputAmperage);

	public static NotifiableEnergyContainer ReceiverContainer(long maxCapacity,
	                                                           long maxInputVoltage, long maxInputAmperage)
		=> new(maxCapacity, maxInputVoltage, maxInputAmperage, 0L, 0L);

	public void ResetBasicInfo(long maxCapacity, long maxInputVoltage, long maxInputAmperage,
	                           long maxOutputVoltage, long maxOutputAmperage)
	{
		EnergyCapacity = maxCapacity;
		InputVoltage   = maxInputVoltage;
		InputAmperage  = maxInputAmperage;
		OutputVoltage  = maxOutputVoltage;
		OutputAmperage = maxOutputAmperage;
		bool isIn  = (InputVoltage  != 0 && InputAmperage  != 0);
		bool isOut = (OutputVoltage != 0 && OutputAmperage != 0);
		HandlerIO = (isIn && isOut) ? IO.BOTH : isIn ? IO.IN : isOut ? IO.OUT : IO.NONE;
		CheckOutputSubscription();
	}

	public long GetInputPerSec()  => _lastEnergyInputPerSec;
	public long GetOutputPerSec() => _lastEnergyOutputPerSec;

	// Verbatim port of setEnergyStored.
	public void SetEnergyStored(long energyStored)
	{
		if (_energyStored == energyStored) return;
		if (energyStored > _energyStored)
			_energyInputPerSec  += energyStored - _energyStored;
		else
			_energyOutputPerSec += _energyStored - energyStored;
		_energyStored = energyStored;
		// Upstream: syncDataHolder.markClientSyncFieldDirty("energyStored");
		// dropped - MachineStateSyncPacket carries the trait Save() blob.
		CheckOutputSubscription();
		NotifyListeners();
	}

	// Verbatim port of onMachineLoad - subscribes the persistent update
	// callback (stats rotation, every-tick) and conditionally subscribes
	// the output-push callback.
	public override void OnMachineLoad()
	{
		base.OnMachineLoad();
		CheckOutputSubscription();
		_updateSubs = SubscribeServerTick(_updateSubs, UpdateTick);
	}

	// Verbatim port of onMachineUnload - cancels the update subscription.
	public override void OnMachineUnload()
	{
		base.OnMachineUnload();
		if (_updateSubs is not null)
		{
			_updateSubs.Unsubscribe();
			_updateSubs = null;
		}
	}

	// Verbatim port of checkOutputSubscription. Subscribes the output-push
	// callback when there's enough stored energy to push at least one packet
	// at OutputVoltage; cancels the subscription when below threshold so an
	// idle generator doesn't burn ticks scanning for receivers.
	// Virtual so EnergyBatteryTrait can gate on `isWorkingEnabled` first
	// (mirrors upstream's override that unsubscribes when paused).
	public virtual void CheckOutputSubscription()
	{
		if (OutputVoltage > 0 && OutputAmperage > 0)
		{
			if (_energyStored >= OutputVoltage)
				_outputSubs = SubscribeServerTick(_outputSubs, ServerTick);
			else if (_outputSubs is not null)
			{
				_outputSubs.Unsubscribe();
				_outputSubs = null;
			}
		}
	}

	// Verbatim port of updateTick - every 20 ticks, rotate per-tick stats.
	private void UpdateTick()
	{
		if (Main.GameUpdateCount % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(20) == 0)
		{
			_lastEnergyOutputPerSec = _energyOutputPerSec;
			_lastEnergyInputPerSec  = _energyInputPerSec;
			_energyOutputPerSec     = 0;
			_energyInputPerSec      = 0;
		}
	}

	// Verbatim port of serverTick - push energy out through every outputting
	// side until amperage budget is spent. Subscribed conditionally by
	// CheckOutputSubscription.
	//
	// `protected virtual` so EnergyBatteryTrait can replace with its
	// battery-distribution discharge loop (mirrors upstream's
	// `EnergyBatteryTrait.serverTick` override).
	protected virtual void ServerTick()
	{
		if (MetaMachine.IsClient) return;
		// Gate adjacent-push to upstream's 20 Hz MC cadence (mirrors
		// RecipeLogic.ServerTick:199). Without this, OutputAmperage is paid
		// out 3x per real second instead of 1x per MC tick.
		if (Main.GameUpdateCount % (uint)global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(1) != 0) return;

		if (_energyStored >= OutputVoltage && OutputVoltage > 0 && OutputAmperage > 0)
		{
			long outputVoltage  = OutputVoltage;
			long outputAmperes  = Math.Min(_energyStored / outputVoltage, OutputAmperage);
			if (outputAmperes == 0) return;
			long amperesUsed = 0;
			// Adaptation: upstream walks GTUtil.DIRECTIONS once (single-block
			// machine). We walk the full multi-tile footprint perimeter so a
			// 2x2 generator can push out of every side, not just the two edges
			// touching its top-left cell. PerimeterNeighbors dedups neighbors.
			foreach (var (side, neighbor) in MachineCellResolver.PerimeterNeighbors(Machine))
			{
				if (!OutputsEnergy(side)) continue;
				var oppositeSide = side.Opposite();
				// Trait<->trait lookup during the IEnergyContainer-interface
				// migration window. After that, swap to
				// WorldCapability.Get<IEnergyContainer>.
				var energyContainer = neighbor.Traits.GetTrait<NotifiableEnergyContainer>(TYPE);
				if (energyContainer != null && energyContainer.InputsEnergy(oppositeSide))
				{
					amperesUsed += energyContainer.AcceptEnergyFromNetwork(
						oppositeSide, outputVoltage, outputAmperes - amperesUsed);
					if (amperesUsed >= outputAmperes) break;
				}
			}
			if (amperesUsed > 0)
				SetEnergyStored(_energyStored - amperesUsed * outputVoltage);
		}
	}

	// Verbatim port of acceptEnergyFromNetwork. Per-tick amp counter resets
	// when the world tick advances. Overvoltage explodes the machine and
	// still returns the would-be amp draw (matches upstream - the cable
	// pushing the overvoltage doesn't get refunded).
	//
	// Virtual so EnergyBatteryTrait can substitute its battery-charging
	// logic (mirrors upstream's override).
	public virtual long AcceptEnergyFromNetwork(IODirection side, long voltage, long amperage)
	{
		long latestTimeStamp = Main.GameUpdateCount;
		if (_lastTimeStamp < latestTimeStamp)
		{
			_amps = 0;
			_lastTimeStamp = latestTimeStamp;
		}
		if (_amps >= InputAmperage) return 0;
		long canAccept = EnergyCapacity - _energyStored;
		if (voltage > 0L && InputsEnergy(side))
		{
			if (voltage > InputVoltage)
			{
				// Verbatim with upstream:
				//   var explodable = getMachine().getTrait(EnvironmentalExplosionTrait.TYPE);
				//   if (explodable != null)
				//     GTUtil.doExplosion(..., GTUtil.getExplosionPower(voltage));
				// No trait attached -> silent reject (machine has no explosion
				// configured). Math contract identical: the offender draws
				// up to its full input-amperage budget regardless.
				var explodable = Machine.Traits.GetTrait(
					Common.Machine.Trait.EnvironmentalExplosionTrait.TYPE);
				if (explodable != null)
				{
					// Diagnostic log - prints once per explosion. Tells us EXACTLY
					// what voltage came in vs what the container was rated for.
					Terraria.ModLoader.ModContent.GetInstance<GregTechCEuTerraria>()
						?.Logger?.Warn(
							$"[overvoltage] {Machine?.GetType().Name ?? "?"} at " +
							$"({Machine?.Position.X},{Machine?.Position.Y}) - pushed {voltage} V " +
							$"vs InputVoltage {InputVoltage} V (side {side}, amperage {amperage}) - EXPLODING");
					explodable.DoExplosion(
						Common.Machine.Trait.EnvironmentalExplosionTrait.GetExplosionPower(voltage));
				}
				return Math.Min(amperage, InputAmperage - _amps);
			}
			if (canAccept >= voltage)
			{
				long amperesAccepted = Math.Min(canAccept / voltage,
				                                 Math.Min(amperage, InputAmperage - _amps));
				if (amperesAccepted > 0)
				{
					SetEnergyStored(_energyStored + voltage * amperesAccepted);
					_amps += amperesAccepted;
					return amperesAccepted;
				}
			}
		}
		return 0;
	}

	// Virtual: bidirectional containers (battery buffer) override to drop
	// the `!OutputsEnergy(side)` mutual-exclusion check. In upstream, that
	// check is paired with `sideOutputCondition = side == front` so a face
	// can be EITHER input OR output but not both. Our facing-less 2D port
	// can't make that per-side distinction, so a bidirectional container
	// would report all-output-never-input without an override.
	public virtual bool InputsEnergy(IODirection side) =>
		!OutputsEnergy(side) && InputVoltage > 0 &&
		(SideInputCondition == null || SideInputCondition(side));

	public virtual bool OutputsEnergy(IODirection side) =>
		OutputVoltage > 0 && (SideOutputCondition == null || SideOutputCondition(side));

	// Wire-net hooks - surfaced via IEnergyContainer (default-impl methods).
	// Made virtual on NEC so EnergyBatteryTrait can override; defaults match
	// the IEnergyContainer default-impl exactly.
	public virtual long GetPushAmperage()
	{
		long v = OutputVoltage;
		if (v <= 0) return 0;
		return System.Math.Min(OutputAmperage, _energyStored / v);
	}

	public virtual void OnEnergyPushedToNetwork(long amps, long voltage)
	{
		long drained = amps * voltage;
		if (drained <= 0) return;
		ChangeEnergy(-drained);
	}

	// Verbatim port of changeEnergy. Used by RecipeLogic (drain while
	// working / fill while generating). NOT for cross-block transfer -
	// callers should use AcceptEnergyFromNetwork instead.
	public long ChangeEnergy(long energyToAdd)
	{
		long oldEnergyStored = _energyStored;
		long newEnergyStored = (EnergyCapacity - oldEnergyStored < energyToAdd)
			? EnergyCapacity
			: (oldEnergyStored + energyToAdd);
		if (newEnergyStored < 0) newEnergyStored = 0;
		SetEnergyStored(newEnergyStored);
		return newEnergyStored - oldEnergyStored;
	}

	public long AddEnergy(long energyToAdd)       => ChangeEnergy(energyToAdd);
	public long RemoveEnergy(long energyToRemove) => -ChangeEnergy(-energyToRemove);
	public long GetEnergyCanBeInserted()          => EnergyCapacity - _energyStored;

	// === Battery slot interaction ============================================
	// Verbatim port of dischargeOrRechargeEnergyContainers + handleElectricItem.
	// Returns true if the stack at slotIndex was modified (charged or
	// discharged). The slot itself is an Item[] (our IItemHandler equivalent
	// of Forge's IItemHandlerModifiable).

	public bool DischargeOrRechargeEnergyContainers(Item[] slots, int slotIndex, bool simulate)
	{
		if (slotIndex < 0 || slotIndex >= slots.Length) return false;
		var stackInSlot = slots[slotIndex];
		if (stackInSlot is null || stackInSlot.IsAir) return false;

		// Upstream copies the stack first; we mutate via the ModItem
		// instance directly. Per-stack NBT survives because our BatteryItem
		// IS a ModItem implementing IElectricItem on its own field state.
		var electricItem = stackInSlot.ModItem as IElectricItem;
		if (electricItem != null)
		{
			if (HandleElectricItem(electricItem, simulate))
				return true;
		}
		// Forge Energy compat branch dropped.
		return false;
	}

	private bool HandleElectricItem(IElectricItem electricItem, bool simulate)
	{
		// Upstream: GTUtil.getTierByVoltage(...) -> lowest tier whose voltage
		// is >= the given voltage. Our VoltageTiers.MinTierForVoltage matches.
		int machineTier   = (int)VoltageTiers.MinTierForVoltage(Math.Max(InputVoltage, OutputVoltage));
		int chargeTier    = Math.Min(machineTier, electricItem.GetTier());
		double chargePct  = EnergyCapacity > 0 ? (double)_energyStored / EnergyCapacity : 0.0;

		// Item is a battery (canProvideChargeExternally) AND we have room to
		// take energy from it. Drain when below 1/3rd capacity and the tier
		// matches.
		if (electricItem.CanProvideChargeExternally() && GetEnergyCanBeInserted() > 0)
		{
			if (chargePct <= 0.33 && chargeTier == machineTier)
			{
				long dischargedBy = electricItem.Discharge(GetEnergyCanBeInserted(),
					machineTier, ignoreTransferLimit: false, externally: true, simulate);
				if (!simulate)
					AddEnergy(dischargedBy);
				return dischargedBy > 0L;
			}
		}

		// Above 2/3rd -> push energy into the item.
		if (chargePct > 0.66)
		{
			long chargedBy = electricItem.Charge(_energyStored, chargeTier,
				ignoreTransferLimit: false, simulate: false);
			if (!simulate)
				RemoveEnergy(chargedBy);
			return chargedBy > 0;
		}
		return false;
	}

	// === IRecipeHandlerTrait<EnergyStack> ===================================
	// Verbatim port of upstream's handleRecipeInner + getContents +
	// getTotalContentAmount + getCapability + getHandlerIO.

	public override IO GetHandlerIO() => HandlerIO;

	public override List<Api.Recipe.Ingredient.EnergyStack>? HandleRecipeInner(
		IO io, GTRecipe recipe, List<Api.Recipe.Ingredient.EnergyStack> left, bool simulate)
	{
		// Reverse-iterate so RemoveAt doesn't shift indices we haven't visited
		// (Java port walks forward via ListIterator with .set/.remove; same
		// effect, different cursor mechanics).
		for (int i = left.Count - 1; i >= 0; i--)
		{
			var stack = left[i];
			if (stack.IsEmpty()) { left.RemoveAt(i); continue; }

			long totalEU = stack.GetTotalEU();
			long canTransfer = Math.Min(totalEU,
				io == IO.IN ? EnergyStored : EnergyCapacity - EnergyStored);
			if (!simulate)
			{
				// invert the EU value if we're doing inputs (inputting *to the
				// recipe* -> removing from handlers)
				ChangeEnergy(io == IO.IN ? -canTransfer : canTransfer);
			}

			totalEU -= canTransfer;
			if (totalEU <= 0)
				left.RemoveAt(i);
			else
				left[i] = new Api.Recipe.Ingredient.EnergyStack(totalEU);
		}
		return left.Count == 0 ? null : left;
	}

	public override IReadOnlyList<object> GetContents()
	{
		// Upstream uses EnergyContainerList.calculateVoltageAmperage which
		// can split stored EU across multiple sub-stacks under specific
		// conditions. We don't have EnergyContainerList yet; this single-
		// stack form is correct for the typical case and lands here for
		// shape parity. Replace with the upstream split-logic when
		// EnergyContainerList is ported.
		long amperage = Math.Max(InputAmperage, OutputAmperage);
		return new object[] { new Api.Recipe.Ingredient.EnergyStack(EnergyStored, Math.Max(1, amperage)) };
	}

	public override double GetTotalContentAmount() => _energyStored;

	public override Api.Capability.Recipe.RecipeCapability<Api.Recipe.Ingredient.EnergyStack>
		GetCapability() => Api.Capability.Recipe.EURecipeCapability.CAP;

	// === Persistence =========================================================
	// Only `energyStored` is persisted in upstream (via @SaveField); the rest
	// is set by the constructor from the machine's tier and re-derived on
	// load. We match.

	public override void Save(TagCompound tag)
	{
		tag["energyStored"] = _energyStored;
	}

	public override void Load(TagCompound tag)
	{
		if (tag.ContainsKey("energyStored"))
			_energyStored = tag.GetLong("energyStored");
	}

	// energyStored is synced to MP clients via the dedicated, compact
	// MachineEnergySyncPacket (per-field, ~12 B), NOT via the full machine state
	// blob. Per-tick energy jitter would otherwise re-serialize + re-send the
	// whole blob every broadcast and defeat MachineStateSync's byte-equality
	// dirty-skip (it was the dominant sync cost - EnergyHatch ~14 KB/s). Mirror
	// of upstream's per-field `@SyncToClient energyStored` (LDLib managed-sync).
	// SaveForSync therefore writes nothing - disk persistence still goes through
	// Save (exact); the join/chunk-load bootstrap rides NetSend->SaveData; a fresh
	// GUI viewer is seeded by MachineEnergySyncPacket.SendTo on view-begin.
	public override void SaveForSync(TagCompound tag) { }

	// Client-only apply of a synced energy value. Sets the display field directly
	// without setEnergyStored's in/out-per-sec stat mutation (those stats are
	// server-side display state, recomputed there; the client just shows the value).
	public void SetStoredFromSync(long energy) => _energyStored = energy;

}
