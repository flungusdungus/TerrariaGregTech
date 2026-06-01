#nullable enable
using System;

namespace GregTechCEuTerraria.Api.Machine;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.machine.TickableSubscription.
// DO NOT modify behavior; mirror upstream changes only.
//
// Handle returned by MetaMachine.SubscribeServerTick. Callers stash the
// handle and call Unsubscribe when they want the per-tick callback to stop.
// The handle itself carries the runnable; the machine's tick walker invokes
// it each tick while StillSubscribed is true and drops it after.
// Upstream declares this as non-final `public class TickableSubscription` -
// subclassing is allowed (no current subclass exists, but parity preserves
// the extensibility point).
public class TickableSubscription
{
	private readonly Action _runnable;
	public bool StillSubscribed { get; private set; }

	public TickableSubscription(Action runnable)
	{
		_runnable = runnable;
		StillSubscribed = true;
	}

	public void Run()
	{
		if (StillSubscribed) _runnable();
	}

	public void Unsubscribe() => StillSubscribed = false;
}
