#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Cover.Data;
using GregTechCEuTerraria.Api.Cover.Filter;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover;

// Port of common.cover.RobotArmCover - ConveyorCover with TransferMode:
// TransferAny / TransferExact (whole configured stacks) / KeepExact (top target
// up to the per-type amount). Adaptations: createUIWidget + copyConfig dropped;
// ItemNetHandler short-circuit omitted; getBuffer/buffer/clearBuffer kept for
// ItemNetHandler.insertOverRobotArm TRANSFER_EXACT accumulation.
public class RobotArmCover : ConveyorCover, ITransferModeCover
{
	private TransferMode _transferMode = TransferMode.TransferAny;
	private int _globalTransferLimit;
	private int _itemsTransferBuffered;

	public RobotArmCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide, int tier)
		: base(definition, coverHolder, attachedSide, tier)
	{
		SetTransferMode(TransferMode.TransferAny);
	}

	public TransferMode TransferMode => _transferMode;
	public int GlobalTransferLimit => _globalTransferLimit;

	// Verbatim getBuffer/buffer/clearBuffer - ItemNetHandler.insertOverRobotArm
	// TRANSFER_EXACT accumulation.
	public int GetBuffer() => _itemsTransferBuffered;
	public void Buffer(int amount) => _itemsTransferBuffered += amount;
	public void ClearBuffer() => _itemsTransferBuffered = 0;

	// Protected upstream; exposed because our ItemNetHandler lives in another
	// area and needs it for per-filter-slot transfer math.
	public Api.Cover.Filter.ItemFilterHandler GetFilterHandler() => FilterHandler;

	public void SetTransferMode(TransferMode transferMode)
	{
		_transferMode = transferMode;
		ConfigureFilter();
	}

	public void SetGlobalTransferLimit(int limit) =>
		_globalTransferLimit = Math.Clamp(limit, 0, _transferMode.MaxStackSize());

	// field 4=transfer mode, 5=per-type limit. 0-3 fall through to ConveyorCover.
	public override void ApplySetting(int field, long value)
	{
		switch (field)
		{
			case 4: SetTransferMode((TransferMode)Math.Clamp(value, 0, 2)); break;
			case 5: SetGlobalTransferLimit((int)value); break;
			default: base.ApplySetting(field, value); break;
		}
	}

	protected override int DoTransferItems(IItemHandler source, IItemHandler target, int maxTransferAmount)
	{
		// Verbatim RobotArmCover.java:62-67 - KEEP_EXACT no-ops on pipe hosts
		// (the pipe-net's InsertOverRobotArm runs KEEP_EXACT during push,
		// counting against the real destination chest). Without this, the
		// virtual pipe handler reads 0 stored and the cover ships N every tick.
		if (_transferMode == TransferMode.KeepExact && CoverHolder is Pipelike.PipeCoverable)
			return 0;

		return _transferMode switch
		{
			TransferMode.TransferAny   => MoveInventoryItems(source, target, maxTransferAmount),
			TransferMode.TransferExact => DoTransferExact(source, target, maxTransferAmount),
			TransferMode.KeepExact     => DoKeepExact(source, target, maxTransferAmount),
			_                          => 0,
		};
	}

	protected int DoTransferExact(IItemHandler sourceInventory, IItemHandler targetInventory, int maxTransferAmount)
	{
		var sourceItemAmount = CountInventoryItemsByType(sourceInventory);

		// Keep only types with the full configured amount available; clamp
		// each surviving group to that amount.
		foreach (int key in new List<int>(sourceItemAmount.Keys))
		{
			var sourceInfo = sourceItemAmount[key];
			int itemToMoveAmount = GetFilteredItemAmount(sourceInfo.ItemStack);
			if (sourceInfo.TotalCount >= itemToMoveAmount) sourceInfo.TotalCount = itemToMoveAmount;
			else sourceItemAmount.Remove(key);
		}

		int itemsTransferred = 0;
		int maxTotalTransferAmount = maxTransferAmount + _itemsTransferBuffered;
		bool notEnoughTransferRate = false;
		foreach (var itemInfo in sourceItemAmount.Values)
		{
			if (maxTotalTransferAmount >= itemInfo.TotalCount)
			{
				bool result = MoveInventoryItemsExact(sourceInventory, targetInventory, itemInfo);
				itemsTransferred       += result ? itemInfo.TotalCount : 0;
				maxTotalTransferAmount -= result ? itemInfo.TotalCount : 0;
			}
			else notEnoughTransferRate = true;
		}
		// Buffer when only the rate blocked us so big exact-stacks accumulate.
		if (itemsTransferred == 0 && notEnoughTransferRate) _itemsTransferBuffered += maxTransferAmount;
		else _itemsTransferBuffered = 0;
		return Math.Min(itemsTransferred, maxTransferAmount);
	}

	protected int DoKeepExact(IItemHandler sourceInventory, IItemHandler targetInventory, int maxTransferAmount)
	{
		var targetItemAmounts = CountInventoryItemsByMatchSlot(targetInventory);
		var sourceItemAmounts = CountInventoryItemsByMatchSlot(sourceInventory);

		// Transfer only the shortfall; drop types already at/above the keep-amount.
		foreach (int key in new List<int>(sourceItemAmounts.Keys))
		{
			var sourceInfo = sourceItemAmounts[key];
			int itemToKeepAmount = GetFilteredItemAmount(sourceInfo.ItemStack);
			int itemAmount = targetItemAmounts.TryGetValue(key, out var dest) ? dest.TotalCount : 0;
			if (itemAmount < itemToKeepAmount) sourceInfo.TotalCount = itemToKeepAmount - itemAmount;
			else sourceItemAmounts.Remove(key);
		}
		return MoveInventoryItems(sourceInventory, targetInventory, sourceItemAmounts, maxTransferAmount);
	}

	private int GetFilteredItemAmount(Item itemStack)
	{
		if (!FilterHandler.IsFilterPresent) return _globalTransferLimit;
		var filter = FilterHandler.GetFilter();
		return filter.SupportsAmounts ? filter.TestItemCount(itemStack) : _globalTransferLimit;
	}

	protected override void ConfigureFilter()
	{
		if (FilterHandler.GetFilter() is SimpleItemFilter filter)
			filter.SetMaxStackSize(filter.IsBlackList ? 1 : _transferMode.MaxStackSize());
	}

	public override void Save(TagCompound tag)
	{
		base.Save(tag);
		tag["transferMode"] = (int)_transferMode;
		tag["transferLimit"] = _globalTransferLimit;
	}

	public override void Load(TagCompound tag)
	{
		// _transferMode MUST be restored BEFORE base.Load. ConveyorCover.Load
		// -> FilterHandler.Load -> OnFilterLoaded -> ConfigureFilter would run with
		// _transferMode still TransferAny -> SetMaxStackSize(1) DESTRUCTIVELY
		// clamps every freshly-loaded match.stack to 1; the post-base.Load
		// SetMaxStackSize(1024) raises the cap but the matches are already lost.
		if (tag.ContainsKey("transferMode")) _transferMode = (TransferMode)tag.GetInt("transferMode");
		if (tag.ContainsKey("transferLimit")) _globalTransferLimit = tag.GetInt("transferLimit");
		base.Load(tag);
	}
}
