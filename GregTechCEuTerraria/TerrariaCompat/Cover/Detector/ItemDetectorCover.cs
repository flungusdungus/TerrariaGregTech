#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Util;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Detector;

// Port of common.cover.detector.ItemDetectorCover. Signal proportional to
// host item-inventory fill, collapsed to binary by DetectorCover.
public class ItemDetectorCover : DetectorCover
{
	public ItemDetectorCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide) { }

	public override bool CanAttach() => base.CanAttach() && GetItemHandler() != null;

	protected override void Update()
	{
		if (CoverHolder.GetOffsetTimer() % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(20) != 0) return;

		var handler = GetItemHandler();
		if (handler == null) return;

		int itemCapacity = handler.SlotCount * handler.GetSlotLimit(0);
		if (itemCapacity == 0) return;

		// Upstream's SKIP_ITEM_DETECTOR tag dropped - every slot counts.
		int storedItems = 0;
		for (int i = 0; i < handler.SlotCount; i++)
			storedItems += handler.GetSlot(i).stack;

		SetRedstoneSignalOutput(RedstoneUtil.ComputeRedstoneValue(storedItems, itemCapacity, IsInverted));
	}

	// Our machine IS the IItemHandler (vs upstream's per-side resolve).
	protected IItemHandler? GetItemHandler() => CoverHolder as IItemHandler;
}
