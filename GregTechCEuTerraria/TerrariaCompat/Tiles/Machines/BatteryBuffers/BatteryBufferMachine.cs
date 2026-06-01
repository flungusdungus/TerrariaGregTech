#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.Common.Machine.Trait;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Machines.BatteryBuffers;

// 1:1 port of com.gregtechceu.gtceu.common.machine.electric.BatteryBufferMachine.
// The nested EnergyBatteryTrait : NotifiableEnergyContainer (below) owns all
// energy state (sum-over-batteries capacity/stored, discharge, charge, output
// subscription); the machine owns State, batteryInventory, isWorkingEnabled.
//
// DEVIATION: no facing system - upstream's side input/output
// conditions (side != / == frontFacing) collapse to sideless I/O on all
// cardinals. Both the trait's direct-adjacent push and the cable pull-model
// share the trait's _amps budget so the per-tick cap is enforced once.
public class BatteryBufferMachine : TieredEnergyMachine, IControllable
{
	public BatteryBufferMachine() { }
	public BatteryBufferMachine(VoltageTier tier) : base(tier) { }

	protected override string Label => Definition?.Label ?? "Battery Buffer";

	// upstream BatteryBufferMachine.java:45-46
	public const long AMPS_PER_BATTERY_NORMAL  = 2L;
	public const long AMPS_PER_BATTERY_CHARGER = 4L;

	public enum State { IDLE, RUNNING, FINISHED }

	// Dimensions from the bound MachineDefinition (upstream ctor params).
	public virtual int  SlotCount        => Definition?.BatterySlotCount ?? 0;
	public virtual long InputAmpsPerItem => Definition?.InputAmpsPerItem  ?? AMPS_PER_BATTERY_NORMAL;
	public virtual long OutputAmps       => Definition?.OutputAmps        ?? 0;

	private bool   _isWorkingEnabled = true;        // upstream @SaveField
	private State  _state            = State.IDLE;  // upstream @SyncToClient

	// IControllable over isWorkingEnabled (battery buffers have no RecipeLogic);
	// setWorkingEnabled rechecks the output subscription (java:170-173).
	bool IControllable.IsWorkingEnabled() => _isWorkingEnabled;
	void IControllable.SetWorkingEnabled(bool enabled)
	{
		_isWorkingEnabled = enabled;
		(EnergyContainer as EnergyBatteryTrait)?.CheckOutputSubscription();
	}

	public State CurrentState => _state;

	// changeState (java:115-123). State-driven render art not wired (active
	// overlays go through MachineRenderer's flag); the field is server-side.
	internal void ChangeState(State newState)
	{
		if (_state == newState) return;
		_state = newState;
	}

	// Battery inventory (upstream `batteryInventory`) - holds any IElectricItem
	// (rechargeable batteries AND electric tools).
	private Item[]? _batteryInv;
	public Item[] BatteryInv
	{
		get
		{
			if (_batteryInv is null)
			{
				_batteryInv = new Item[SlotCount];
				for (int i = 0; i < SlotCount; i++) _batteryInv[i] = new Item();
			}
			return _batteryInv;
		}
	}

	public override Item[]? GetSlotGroup(SlotGroup group) => group switch
	{
		SlotGroup.Inventory => BatteryInv,
		_ => base.GetSlotGroup(group),
	};

	// Electric-item list helpers (java:175-225) - verbatim predicates + iteration
	// order. No up-front chargeable/tier check (gated inside charge/discharge);
	// keeping all items in the list matches upstream's count math
	// (distributed = energy / size, amp budget = size * inputAmpsPerItem).
	internal List<IElectricItem> GetNonFullBatteries()
	{
		var result = new List<IElectricItem>();
		var inv = BatteryInv;
		for (int i = 0; i < inv.Length; i++)
		{
			if (inv[i].IsAir) continue;
			if (inv[i].ModItem is not IElectricItem e) continue;
			if (e.GetCharge() < e.GetMaxCharge()) result.Add(e);
		}
		return result;
	}

	internal List<IElectricItem> GetNonEmptyBatteries()
	{
		var result = new List<IElectricItem>();
		var inv = BatteryInv;
		for (int i = 0; i < inv.Length; i++)
		{
			if (inv[i].IsAir) continue;
			if (inv[i].ModItem is not IElectricItem e) continue;
			if (e.CanProvideChargeExternally() && e.GetCharge() > 0) result.Add(e);
		}
		return result;
	}

	internal List<IElectricItem> GetAllBatteries()
	{
		var result = new List<IElectricItem>();
		var inv = BatteryInv;
		for (int i = 0; i < inv.Length; i++)
		{
			if (inv[i].IsAir) continue;
			if (inv[i].ModItem is IElectricItem e) result.Add(e);
		}
		return result;
	}

	// Charger subclass overrides OutputAmps=0 to suppress discharge; CanExtract
	// then returns false so the network classifies us as receive-only.
	public override bool CanAccept  => true;
	public override bool CanExtract => OutputAmps > 0;

	// Trait computes capacity by summing batteries each access.
	public override long EnergyCapacity => EnergyContainer.EnergyCapacity;

	// Substitutes EnergyBatteryTrait for the default (upstream java:78-80 ctor arg).
	protected override NotifiableEnergyContainer CreateEnergyContainer()
		=> new EnergyBatteryTrait(SlotCount, InputAmpsPerItem, OutputAmps, Tier);

	// Opt OUT of the compact energy-sync channel: EnergyBatteryTrait.EnergyStored
	// is DERIVED from the battery items (it ignores the base _energyStored field),
	// and those items already ride the state blob. Sending an energy packet here
	// would write a value the client's EnergyStored override ignores - pure waste.
	public override bool HasSyncEnergy => false;

	// Push semantics live on EnergyBatteryTrait (GetPushAmperage,
	// OnEnergyPushedToNetwork); wire-net calls them through IEnergyContainer.
	public override long AcceptEnergy(long amount, VoltageTier sourceTier)
	{
		var trait = EnergyContainer as EnergyBatteryTrait;
		if (trait is null) return 0;
		long voltage  = VoltageTiers.Voltage(sourceTier);
		long amperage = voltage > 0 ? Math.Max(1, amount / voltage) : 0;
		if (amperage <= 0) return 0;
		long usedAmps = trait.AcceptEnergyFromNetwork(IODirection.Up, voltage, amperage);
		return usedAmps * voltage;  // EU equivalent for the network's accounting
	}

	// Energy state lives on the trait (Traits.Save); machine persists only
	// isWorkingEnabled + the battery inventory (java:64-72).
	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["isWorkingEnabled"] = _isWorkingEnabled;
		var inv = BatteryInv;
		var list = new List<TagCompound>(inv.Length);
		for (int i = 0; i < inv.Length; i++)
			list.Add(ItemIO.Save(inv[i]));
		tag["BatteryInv"] = list;
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		_isWorkingEnabled = !tag.ContainsKey("isWorkingEnabled") || tag.GetBool("isWorkingEnabled");

		// Legacy NBT key migration (java:101-105) - older saves used "chargerInventory".
		string? invKey = tag.ContainsKey("BatteryInv") ? "BatteryInv"
		             : (tag.ContainsKey("chargerInventory") ? "chargerInventory" : null);
		if (invKey != null)
		{
			var list = tag.GetList<TagCompound>(invKey);
			var inv = BatteryInv;
			for (int i = 0; i < inv.Length && i < list.Count; i++)
				inv[i] = ItemIO.Load(list[i]);
		}
	}

	public override void AppendTooltip(List<string> lines)
	{
		base.AppendTooltip(lines);
		lines.Add($"Battery slots: {SlotCount}");
		lines.Add($"Input: {EnergyContainer.InputAmperage}A at {EnergyContainer.InputVoltage:N0} EU/t");
		if (OutputAmps > 0)
			lines.Add($"Output: {OutputAmps}A at {EnergyContainer.OutputVoltage:N0} EU/t");
		lines.Add($"State: {_state}");
	}

	// 1:1 port of upstream's nested static class (java:238-426). Overrides 5
	// NotifiableEnergyContainer virtuals to substitute battery-distribution
	// semantics for the default single-field EU buffer.
	public sealed class EnergyBatteryTrait : NotifiableEnergyContainer
	{
		private readonly VoltageTier _tier;
		private readonly long _inputAmpsPerItem;

		// upstream ctor java:243-252
		public EnergyBatteryTrait(int inventorySize, long inputAmpsPerItem, long outputAmps, VoltageTier tier)
			: base(VoltageTiers.Voltage(tier) * inventorySize * 32L,         // maxCapacity
			       VoltageTiers.Voltage(tier),                                // maxInputVoltage
			       inventorySize * inputAmpsPerItem,                          // maxInputAmperage
			       outputAmps == 0 ? 0 : VoltageTiers.Voltage(tier),          // maxOutputVoltage
			       outputAmps)                                                // maxOutputAmperage
		{
			_tier = tier;
			_inputAmpsPerItem = inputAmpsPerItem;
			// DEVIATION: no front-facing system - only the
			// isWorkingEnabled gate (upstream also gates side != / == front).
			SideInputCondition  = _ => Buffer.WorkingEnabled;
			SideOutputCondition = _ => Buffer.WorkingEnabled;
		}

		// DEVIATION: drop NEC's `!OutputsEnergy(side)` mutual-exclusion
		// check. Upstream pairs it with `sideOutputCondition = side == front` so
		// non-front faces stay input-capable; our facing-less port has the same
		// condition on all sides, which would make InputsEnergy false everywhere
		// (buffer accepts no adjacent push). Trade-off: two adjacent buffers can
		// pingpong, but the per-tick _amps budget bounds the loss.
		public override bool InputsEnergy(IODirection side) =>
			InputVoltage > 0 && (SideInputCondition is null || SideInputCondition(side));

		private BatteryBufferMachine Buffer => (BatteryBufferMachine)Machine;

		protected override IReadOnlyList<Type> ValidMachineClasses() =>
			new[] { typeof(BatteryBufferMachine) };

		// checkOutputSubscription (java:266-272)
		public override void CheckOutputSubscription()
		{
			if (Buffer.WorkingEnabled)
			{
				base.CheckOutputSubscription();
			}
			else if (_outputSubs is not null)
			{
				_outputSubs.Unsubscribe();
				_outputSubs = null;
			}
		}

		// serverTick (java:275-319). Upstream pushes to the front neighbor; we
		// walk all 4 cardinals (no facing). Amp budget applies once per tick.
		protected override void ServerTick()
		{
			if (MetaMachine.IsClient) return;
			// 20 Hz MC cadence gate (override doesn't call base, so base gate is bypassed).
			if (Main.GameUpdateCount % (uint)global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(1) != 0) return;

			long voltage = OutputVoltage;
			if (voltage <= 0 || OutputAmperage <= 0) return;
			if (!Buffer.WorkingEnabled) return;

			var batteries = Buffer.GetNonEmptyBatteries();
			if (batteries.Count == 0) return;

			// Upstream: `internalAmps = abs(min(0, getInternalStorage() / voltage))`
			// - only counts NEGATIVE internalStorage (the rounding-residual case).
			// `_energyStored` is the upstream `internalStorage` shim.
			long internalAmps = Math.Abs(Math.Min(0, _energyStored / voltage));
			long genAmps      = Math.Max(0, batteries.Count - internalAmps);
			long outAmps      = 0L;

			if (genAmps > 0)
			{
				// Accumulate across all output cardinals (upstream pushes one direction).
				long remaining = genAmps;
				foreach (var (side, neighbor) in MachineCellResolver.PerimeterNeighbors(Machine))
				{
					if (!OutputsEnergy(side)) continue;
					if (remaining <= 0) break;
					var opposite = side.Opposite();
					var nc = neighbor.Traits.GetTrait<NotifiableEnergyContainer>(TYPE);
					if (nc is null || !nc.InputsEnergy(opposite)) continue;
					long accepted = nc.AcceptEnergyFromNetwork(opposite, voltage, remaining);
					outAmps   += accepted;
					remaining -= accepted;
				}
				if (outAmps == 0 && internalAmps == 0) return;
			}

			long energy      = (outAmps + internalAmps) * voltage;
			long distributed = energy / batteries.Count;

			bool changed = false;
			foreach (var b in batteries)
			{
				// upstream:303 discharge(distributed, tier, false, true, false).
				long charged = b.Discharge(distributed, (int)_tier,
					ignoreTransferLimit: false, externally: true, simulate: false);
				if (charged > 0) changed = true;
				energy -= charged;
				_energyOutputPerSec += charged;
			}

			if (changed)
			{
				Buffer.ChangeState(State.RUNNING);
				CheckOutputSubscription();
			}

			// Upstream:317 `setEnergyStored(getInternalStorage() + internalAmps * voltage - energy)`
			SetEnergyStored(_energyStored + internalAmps * voltage - energy);
		}

		// acceptEnergyFromNetwork (java:322-383)
		public override long AcceptEnergyFromNetwork(IODirection side, long voltage, long amperage)
		{
			long latestTimeStamp = Main.GameUpdateCount;
			if (_lastTimeStamp < latestTimeStamp)
			{
				_amps = 0;
				_lastTimeStamp = latestTimeStamp;
			}
			if (amperage <= 0 || voltage <= 0)
			{
				Buffer.ChangeState(State.IDLE);
				return 0;
			}

			var batteries = Buffer.GetNonFullBatteries();
			long leftAmps = batteries.Count * _inputAmpsPerItem - _amps;
			long usedAmps = Math.Min(leftAmps, amperage);
			if (leftAmps <= 0) return 0;

			if (SideInputCondition == null || SideInputCondition(side))
			{
				if (voltage > InputVoltage)
				{
					// Upstream calls GTUtil.doExplosion directly (no trait
					// presence-check, unlike NotifiableEnergyContainer).
					ExplodeOnOvervoltage(voltage);
					return usedAmps;
				}

				long internalAmps = Math.Min(leftAmps, Math.Max(0, _energyStored / voltage));
				usedAmps          = Math.Min(usedAmps, leftAmps - internalAmps);
				_amps            += usedAmps;

				long energy      = (usedAmps + internalAmps) * voltage;
				long distributed = batteries.Count > 0 ? energy / batteries.Count : 0;

				bool changed = false;
				foreach (var b in batteries)
				{
					// upstream:358 charge(min(distributed, V[item.tier] *
					// inputAmpsPerItem), tier, ignoreTransferLimit=true, false).
					long cap     = VoltageTiers.Voltage((VoltageTier)b.GetTier()) * _inputAmpsPerItem;
					long charged = b.Charge(Math.Min(distributed, cap), (int)_tier,
						ignoreTransferLimit: true, simulate: false);
					if (charged > 0) changed = true;
					energy -= charged;
					_energyInputPerSec += charged;
				}

				if (changed)
				{
					Buffer.ChangeState(State.RUNNING);
					CheckOutputSubscription();
				}

				// Upstream:379 `setEnergyStored(getInternalStorage() - internalAmps * voltage + energy)`.
				SetEnergyStored(_energyStored - internalAmps * voltage + energy);
				return usedAmps;
			}
			return 0;
		}

		// getEnergyCapacity (java:386-401)
		public override long EnergyCapacity
		{
			get
			{
				long cap = 0;
				foreach (var b in Buffer.GetAllBatteries()) cap += b.GetMaxCharge();
				if (cap == 0) Buffer.ChangeState(State.IDLE);
				return cap;
			}
		}

		// getEnergyStored (java:404-421)
		public override long EnergyStored
		{
			get
			{
				long stored = 0;
				long cap    = 0;
				foreach (var b in Buffer.GetAllBatteries())
				{
					stored += b.GetCharge();
					cap    += b.GetMaxCharge();
				}
				if (cap != 0 && cap == stored) Buffer.ChangeState(State.FINISHED);
				return stored;
			}
		}

		// Amps we'd offer the cable network this tick (gen-amps math from
		// ServerTick, exposed for the pull-model path).
		internal long ComputeAvailableOutputAmps()
		{
			if (!Buffer.WorkingEnabled || OutputVoltage <= 0) return 0;
			long voltage = OutputVoltage;
			long internalAmps = Math.Abs(Math.Min(0, _energyStored / voltage));
			long nonEmpty     = Buffer.GetNonEmptyBatteries().Count;
			return Math.Min(OutputAmperage, Math.Max(0, nonEmpty - internalAmps) + internalAmps);
		}

		// Wire-net hooks: push amperage depends on non-empty battery count, and
		// the drain distributes across batteries (not a flat _energyStored decrement).
		public override long GetPushAmperage() => ComputeAvailableOutputAmps();

		public override void OnEnergyPushedToNetwork(long amps, long voltage)
			=> DistributeEnergyOut(amps, voltage);

		// Pull-model equivalent of the ServerTick discharge loop, called after
		// the network decides how many offered amps it took.
		internal void DistributeEnergyOut(long amps, long voltage)
		{
			if (amps <= 0 || voltage <= 0) return;
			long latestTimeStamp = Main.GameUpdateCount;
			if (_lastTimeStamp < latestTimeStamp)
			{
				_amps = 0;
				_lastTimeStamp = latestTimeStamp;
			}
			var batteries = Buffer.GetNonEmptyBatteries();
			if (batteries.Count == 0) return;

			long internalAmps = Math.Max(0, _energyStored / voltage);
			long outAmps      = Math.Min(amps, Math.Max(0, batteries.Count));

			long energy      = (outAmps + internalAmps) * voltage;
			long distributed = energy / batteries.Count;

			bool changed = false;
			foreach (var b in batteries)
			{
				long charged = b.Discharge(distributed, (int)_tier,
					ignoreTransferLimit: false, externally: true, simulate: false);
				if (charged > 0) changed = true;
				energy -= charged;
				_energyOutputPerSec += charged;
			}
			SetEnergyStored(_energyStored + internalAmps * voltage - energy);
			if (changed) Buffer.ChangeState(State.RUNNING);
		}

		// Upstream (java:339) calls GTUtil.doExplosion directly, bypassing the
		// trait presence-check NEC.acceptEnergyFromNetwork uses.
		private void ExplodeOnOvervoltage(long voltage) =>
			EnvironmentalExplosionTrait.DoExplosionAt(Machine,
				EnvironmentalExplosionTrait.GetExplosionPower(voltage));
	}
}
