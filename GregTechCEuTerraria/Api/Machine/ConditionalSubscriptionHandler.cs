#nullable enable
using System;
using GregTechCEuTerraria.Api.Cover;

namespace GregTechCEuTerraria.Api.Machine;

// Port of com.gregtechceu.gtceu.api.machine.ConditionalSubscriptionHandler.
//
// Handles a server-tick subscription that is only active while a condition
// holds - when the condition is false the subscription is removed so it
// doesn't consume resources.
//
// Documented adaptations:
//   - The handler is an ICoverable (the cover holder) rather than an
//     ITickSubscription - that is the surface a CoverBehavior has access to.
//   - initialize(Level)/initialize(BlockableEventLoop) - which posted a
//     TickTask to defer the first updateSubscription by one tick - collapses to
//     Initialize() calling UpdateSubscription() directly. Covers run Initialize
//     from OnLoad, which is already server-side and after the holder exists.
//   - subscribeServerTick(last, runnable)'s "reuse if still alive" semantics
//     are reproduced inline (reuse the handle unless it has been unsubscribed).
public sealed class ConditionalSubscriptionHandler
{
	private readonly ICoverable _holder;
	private readonly Action _runnable;
	private readonly Func<bool> _condition;

	private TickableSubscription? _subscription;

	public ConditionalSubscriptionHandler(ICoverable holder, Action runnable, Func<bool> condition)
	{
		_holder = holder;
		_runnable = runnable;
		_condition = condition;
	}

	public void Initialize() => UpdateSubscription();

	public void UpdateSubscription()
	{
		if (_condition())
		{
			if (_subscription is null || !_subscription.StillSubscribed)
				_subscription = _holder.SubscribeServerTick(_runnable);
		}
		else if (_subscription != null)
		{
			_subscription.Unsubscribe();
			_subscription = null;
		}
	}

	public void Unsubscribe()
	{
		if (_subscription != null)
		{
			_subscription.Unsubscribe();
			_subscription = null;
		}
	}
}
