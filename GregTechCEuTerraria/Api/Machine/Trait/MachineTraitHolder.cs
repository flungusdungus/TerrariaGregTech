#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Machine;
using System;
using System.Collections.Generic;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Machine.Trait;

// LOCKED - port of com.gregtechceu.gtceu.api.machine.trait.MachineTraitHolder.
//
// Owned by MetaMachine. Holds the attached trait list, indexes them by
// MachineTraitType, and dispatches lifecycle events.
//
// Adapted: upstream's NBT persistence goes through Forge's ValueTransformer +
// SyncDataHolder pipeline; we use tML's TagCompound directly. Sync to clients
// is handled separately by MachineStateSyncPacket - traits with sync needs
// expose their state through Save/Load and the packet picks them up.
public sealed class MachineTraitHolder
{
	private readonly MetaMachine _machine;
	private readonly List<MachineTrait> _traits = new();
	private readonly Dictionary<MachineTraitType, List<MachineTrait>> _traitsByType = new();
	private readonly Dictionary<string, MachineTrait> _persistent = new();

	public MachineTraitHolder(MetaMachine machine)
	{
		_machine = machine;
	}

	// Unmodifiable view - callers iterate, holder owns mutation. Mirrors
	// upstream's `getAllTraits() : @Unmodifiable List<MachineTrait>`.
	public IReadOnlyList<MachineTrait> AllTraits => _traits;

	// Attach a trait at default priority (1). Calls SetMachine (which throws
	// if already attached). Enforces single-instance for trait types that
	// declare AllowMultipleInstances = false.
	public T Attach<T>(T trait) where T : MachineTrait => Attach(trait, callbackPriority: 1);

	// Attach a trait with an explicit callback priority. Higher priorities
	// fire first in dispatch order. Matches upstream's
	// `attachTrait(trait, callbackPriority)`.
	public T Attach<T>(T trait, int callbackPriority) where T : MachineTrait
	{
		trait.TraitPriority = callbackPriority;
		var traitType = trait.TraitType;
		if (!_traitsByType.TryGetValue(traitType, out var list))
		{
			list = new List<MachineTrait>(1);
			_traitsByType[traitType] = list;
		}
		if (!traitType.AllowMultipleInstances && list.Count > 0)
			throw new InvalidOperationException(
				$"Attempted to attach multiple traits of single-instance type: {trait.GetType().Name}");
		list.Add(trait);
		list.Sort(static (a, b) => b.TraitPriority.CompareTo(a.TraitPriority));
		_traits.Add(trait);
		_traits.Sort(static (a, b) => b.TraitPriority.CompareTo(a.TraitPriority));
		trait.SetMachine(_machine);
		// Late attach after the bulk OnMachineLoad pass - fire it directly on
		// the new trait so it can subscribe ticks / etc. WTM's lazy EnsureTraits
		// path hits this on fresh placement.
		if (_machineLoaded) trait.OnMachineLoad();
		return trait;
	}

	// Register a trait under a unique save key. Trait must already be
	// attached via Attach. The Save/Load forwarding writes/reads each
	// persistent trait under tag[key].
	public MachineTraitHolder RegisterPersistent(string key, MachineTrait trait)
	{
		if (trait.Machine != _machine)
			throw new InvalidOperationException("Trait does not belong to this machine.");
		if (_persistent.ContainsKey(key))
			throw new InvalidOperationException($"Duplicate persistent trait key: \"{key}\"");
		_persistent[key] = trait;
		return this;
	}

	// Typed lookup - returns the first (highest-priority) trait of the
	// requested type. Returns null if none attached. T is inferred from
	// the type-token's generic parameter, mirroring upstream's
	// `<T extends MachineTrait> T getTrait(MachineTraitType<T> type)`.
	public T? GetTrait<T>(MachineTraitType<T> type) where T : MachineTrait
	{
		if (!_traitsByType.TryGetValue(type, out var list) || list.Count == 0) return null;
		return type.CastTrait(list[0]);
	}

	// Look up a persistent trait by its registered save key. Verbatim port
	// of upstream's `getPersistentTrait(String traitName)`. Used by save-load
	// code that addresses traits by name rather than by type (e.g. mid-save
	// migration that walks specific keys).
	public T? GetPersistentTrait<T>(string key) where T : MachineTrait =>
		_persistent.GetValueOrDefault(key) as T;

	// All traits of a type - usually used when AllowMultipleInstances = true
	// (e.g. enumerate every NotifiableFluidTank to find one matching a key).
	// Mirrors upstream's `<T extends MachineTrait> List<T> getTraits(MachineTraitType<T> type)`.
	public IReadOnlyList<T> GetTraits<T>(MachineTraitType<T> type) where T : MachineTrait
	{
		if (!_traitsByType.TryGetValue(type, out var list)) return System.Array.Empty<T>();
		var result = new List<T>(list.Count);
		foreach (var t in list) result.Add(type.CastTrait(t));
		return result;
	}

	// === Lifecycle dispatch - called from MetaMachine ===================
	// `_machineLoaded` flips after the first OnMachineLoad so traits attached
	// AFTER load (WTM's lazy EnsureTraits in the first OnTick) still get an
	// OnMachineLoad callback - without it, fresh-placement entities (no LoadData
	// in SP) would never fire RecipeLogic's tick-subscription registration.

	private bool _machineLoaded;

	// Snapshot `_traits` before dispatch - a trait's OnMachineLoad/Unload could
	// re-enter Attach() and mutate `_traits`, and C#'s enumerator throws on any
	// mid-iter mutation. Defensive (no trait re-attaches in its own load today).
	public void OnMachineLoad()
	{
		_machineLoaded = true;
		foreach (var t in _traits.ToArray()) t.OnMachineLoad();
	}
	public void OnMachineUnload()    { foreach (var t in _traits.ToArray()) t.OnMachineUnload();    }
	public void OnMachineDestroyed() { foreach (var t in _traits.ToArray()) t.OnMachineDestroyed(); }
	// Per-tick dispatch is handled by MetaMachine's subscription
	// walker, not here. Traits opt in via SubscribeServerTick; the holder no
	// longer ticks every trait every tick.

	// === Persistence ========================================================
	// Persistent traits get their own tag subtree under the registered key.
	// Non-persistent traits (most behavior-only traits) are skipped.

	public void Save(TagCompound tag)
	{
		foreach (var (key, trait) in _persistent)
		{
			var sub = new TagCompound();
			trait.Save(sub);
			tag[key] = sub;
		}
	}

	// Wire-only walker. Default trait.SaveForSync delegates to Save, so this
	// produces a byte-identical blob unless a trait overrides. Called from
	// MachineStateSyncPacket via MetaMachine.SaveDataForSync.
	public void SaveForSync(TagCompound tag)
	{
		foreach (var (key, trait) in _persistent)
		{
			var sub = new TagCompound();
			trait.SaveForSync(sub);
			tag[key] = sub;
		}
	}

	public void Load(TagCompound tag)
	{
		foreach (var (key, trait) in _persistent)
		{
			if (!tag.ContainsKey(key)) continue;
			// Get<TagCompound> THROWS if a legacy save wrote a non-tag under this
			// key (the old `recipe`-key collision) - skip so the trait loads at
			// defaults instead of crashing world load. (Don't guard with
			// `tag[key] is TagCompound` - tML's indexer doesn't report a stored
			// TagCompound as one, so that drops VALID sub-tags.) trait.Load runs
			// OUTSIDE the catch so a real deserializer bug crashes loudly instead
			// of silently resetting the machine.
			TagCompound sub;
			try { sub = tag.Get<TagCompound>(key); }
			catch (System.Exception) { continue; }
			trait.Load(sub);
		}
	}

	// Per-tick client-side advance for fields omitted from SaveForSync.
	public void OnClientTick()
	{
		foreach (var trait in _traits) trait.OnClientTick();
	}
}
