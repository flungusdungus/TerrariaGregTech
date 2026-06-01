#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Fluids;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Voiding;

// Port of common.cover.voiding.FluidVoidingCover. Fluid mirror of
// ItemVoidingCover; extends PumpCover. Ships DISABLED.
public class FluidVoidingCover : PumpCover
{
	public FluidVoidingCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide, 0)
	{
		SetWorkingEnabled(false);
	}

	protected override bool IsSubscriptionActive() => IsWorkingEnabled();

	protected override void Update()
	{
		if (CoverHolder.GetOffsetTimer() % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(5) != 0) return;

		DoVoidFluids();
		SubscriptionHandler.UpdateSubscription();
	}

	protected virtual void DoVoidFluids()
	{
		var fluidHandler = GetOwnFluidHandler();
		if (fluidHandler == null) return;
		VoidAny(fluidHandler);
	}

	protected void VoidAny(IFluidHandler fluidHandler)
	{
		var fluidAmounts = EnumerateDistinctFluids(fluidHandler, TransferDirection.Extract);

		foreach (var entry in fluidAmounts)
		{
			var stack = entry.Key;
			if (!FilterHandler.Test(stack)) continue;

			foreach (int op in Split(entry.Value))
				fluidHandler.Drain(stack.WithAmount(op), simulate: false);
		}
	}

	// Verbatim GTMath.split - long -> Int32-sized chunks for above-int.MaxValue
	// totals drained tank-by-tank.
	protected static int[] Split(long value)
	{
		var result = new List<int>();
		while (value > 0)
		{
			int intValue = (int)System.Math.Min(value, int.MaxValue);
			result.Add(intValue);
			value -= intValue;
		}
		return result.ToArray();
	}
}
