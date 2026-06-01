#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria.DataStructures;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Machine.Trait;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.machine.trait.MachineTrait.
// DO NOT modify behavior; mirror upstream changes only.
//
// Sidecar state+behavior object: a machine composes trait instances instead of
// inheriting a god-class. The holder forwards lifecycle events; ticking is
// opt-in via SubscribeServerTick (non-subscribers never tick).
//
// Adaptations: SyncDataHolder -> MachineStateSyncPacket; Direction -> IODirection;
// BlockPos -> Point16, no Level; Forge render-state hooks -> MachineTile.PostDraw.
public abstract class MachineTrait
{
	private MetaMachine? _machine;

	// Back-reference to the owning machine. Throws if accessed before
	// attachment (matches upstream IllegalStateException).
	public MetaMachine Machine =>
		_machine ?? throw new InvalidOperationException("Machine trait not attached to machine.");

	// Identity token used for typed lookup via MachineTraitHolder.GetTrait<T>.
	public abstract MachineTraitType TraitType { get; }

	// === Capability gating ====================================================
	// Per-side predicate that decides whether this trait answers capability
	// queries from a given side. Default accepts all sides (null IODirection
	// is treated as "internal / any side"). Covers override this to filter
	// per-side I/O.
	public Predicate<IODirection?> CapabilityValidator { get; set; } = _ => true;

	public bool HasCapability(IODirection? side) => CapabilityValidator(side);

	// === Trait priority =======================================================
	// Higher-priority traits fire callbacks first. The holder iterates the
	// priority-sorted list during dispatch. Covers that override default
	// machine behavior register with higher priority than the underlying
	// notifiable handler so the cover gets first chance to handle the event.
	public int TraitPriority { get; set; } = 1;

	// === Allowed machine classes =============================================
	// A list of machine classes this trait can be attached to. If empty, the
	// trait can be attached to any machine. Holder validates against this
	// list at AttachTrait time.
	protected virtual IReadOnlyList<Type> ValidMachineClasses() => Array.Empty<Type>();

	// Internal seam: called by MachineTraitHolder.AttachTrait. Validates
	// machine class against ValidMachineClasses and stores the back-reference.
	internal void SetMachine(MetaMachine machine)
	{
		if (_machine != null)
			throw new InvalidOperationException("Machine trait already attached to a machine.");
		var allowed = ValidMachineClasses();
		if (allowed.Count > 0)
		{
			bool ok = false;
			foreach (var t in allowed) { if (t.IsInstanceOfType(machine)) { ok = true; break; } }
			if (!ok)
				throw new ArgumentException(
					$"Attempted to attach trait to invalid machine class {machine.GetType().Name}");
		}
		_machine = machine;
	}

	// === Tick subscription forwarding =========================================
	// Forwards to the machine. Subclass calls this in OnMachineLoad to opt
	// into per-tick callbacks. Returns a TickableSubscription handle the
	// trait must keep + cancel in OnMachineUnload.
	//
	// The `last` argument is the previous subscription handle from a prior
	// call - passing it lets the machine cancel the previous one before
	// adding the new (idempotent re-subscribe pattern, matches upstream).
	public TickableSubscription? SubscribeServerTick(TickableSubscription? last, Action runnable)
		=> ((Api.Blockentity.ITickSubscription)Machine).SubscribeServerTick(last, runnable);

	public TickableSubscription? SubscribeServerTick(Action runnable)
		=> Machine.SubscribeServerTick(runnable);

	public void Unsubscribe(TickableSubscription? current) => Machine.Unsubscribe(current);

	// === Position helper ======================================================
	public Point16 BlockPos => Machine.Position;

	// True when the machine is running on a multiplayer client (read-only
	// snapshot, no state mutation). Mirrors upstream isRemote().
	public bool IsRemote => MetaMachine.IsClient;

	// Forge's markAsChanged (save-this-BE-next-save). No-op: tML saves all
	// TileEntities unconditionally. Kept for API parity.
	public void MarkAsChanged() { /* tML saves unconditionally */ }

	// === Lifecycle hooks - forwarded by MachineTraitHolder ===================

	// Fired once when the machine is loaded (post-LoadData, before first tick).
	public virtual void OnMachineLoad() { }

	// Fired when the machine is unloaded (world unload, mod reload).
	public virtual void OnMachineUnload() { }

	// Fired when the machine tile is destroyed (player breaks it, exploded).
	public virtual void OnMachineDestroyed() { }

	// Fired when a neighboring tile changes. No Terraria caller fires this (tML
	// has no per-tile neighbor-change event); virtual stub for API parity.
	// neighborTileType mirrors upstream's `Block neighborBlock` (we have no Block).
	public virtual void OnMachineNeighborChanged(ushort neighborTileType, Point16 neighborPos, bool isMoving) { }

	// === Persistence ========================================================
	// Traits registered via MachineTraitHolder.RegisterPersistent get their
	// own TagCompound subtree under the registered key. Defaults are no-op
	// so non-persistent traits stay invisible to NBT.
	public virtual void Save(TagCompound tag) { }
	public virtual void Load(TagCompound tag) { }

	// Wire-only snapshot for MachineStateSyncPacket; default = Save. Override to
	// omit fields the client derives locally (e.g. monotonic recipe progress).
	// Mirrors upstream @SyncToClient: progress++ isn't dirtied so it's never sent,
	// and omitting it here is what lets the packet's byte-equality skip fire on a
	// running machine. Anything omitted MUST be advanced by OnClientTick (or be
	// cosmetic). See RecipeLogic.
	public virtual void SaveForSync(TagCompound tag) => Save(tag);

	// Fires every tick on MP clients only (server uses the trait's existing
	// subscription path). Default no-op. Override to advance fields that
	// SaveForSync omits so the local view stays in sync between broadcasts.
	public virtual void OnClientTick() { }
}
