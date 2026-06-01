#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Voiding;

// Port of common.cover.voiding.ItemVoidingCover. Voids host items every 5
// ticks; extends ConveyorCover for filter+subscription scaffolding.
// Verbatim ships DISABLED - power-button enables via the cover GUI.
public class ItemVoidingCover : ConveyorCover
{
	public ItemVoidingCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide, 0)
	{
		SetWorkingEnabled(false);
	}

	protected override bool IsSubscriptionActive() => IsWorkingEnabled();

	protected override void Update()
	{
		if (CoverHolder.GetOffsetTimer() % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(5) != 0) return;

		DoVoidItems();
		SubscriptionHandler.UpdateSubscription();
	}

	protected virtual void DoVoidItems()
	{
		var handler = GetOwnItemHandler();
		if (handler == null) return;
		VoidAny(handler);
	}

	protected void VoidAny(IItemHandler handler)
	{
		var filter = FilterHandler.GetFilter();
		for (int slot = 0; slot < handler.SlotCount; slot++)
		{
			Item sourceStack = handler.Extract(slot, int.MaxValue, true);
			if (sourceStack.IsAir || !filter.Test(sourceStack)) continue;
			handler.Extract(slot, int.MaxValue, false);
		}
	}
}
