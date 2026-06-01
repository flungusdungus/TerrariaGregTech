#nullable enable
using System;
using GregTechCEuTerraria.Api.Machine;

namespace GregTechCEuTerraria.Api.Blockentity;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.blockentity.ITickSubscription.
// DO NOT modify behavior; mirror upstream changes only.
//
// Interface for any entity that can host tickable subscriptions. Splits the
// subscription API from the entity class so traits can call
// `Machine.SubscribeServerTick(...)` without coupling to a specific entity
// type.
//
// The default 2-arg overload implements upstream's "reuse if alive" semantics:
// passing a still-subscribed handle returns it unchanged; only when the handle
// is null or already cancelled does it create a new subscription. The runnable
// argument is IGNORED when last is alive - callers wanting to swap the
// runnable must explicitly Unsubscribe(last) first.
public interface ITickSubscription
{
	// Subscribe a runnable to per-tick callbacks. Returns null when the
	// machine is on a client (no server ticks there).
	TickableSubscription? SubscribeServerTick(Action runnable);

	// Cancel a subscription. No-op if `current` is null.
	void Unsubscribe(TickableSubscription? current);

	// Idempotent re-subscribe: reuse `last` if still alive, else create new.
	TickableSubscription? SubscribeServerTick(TickableSubscription? last, Action runnable)
	{
		if (last is null || !last.StillSubscribed)
			return SubscribeServerTick(runnable);
		return last;
	}
}
