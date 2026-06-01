#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Cover.Data;
using GregTechCEuTerraria.Api.Cover.Filter;
using GregTechCEuTerraria.Api.Machine;
using GregTechCEuTerraria.Api.Transfer;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover;

// Port of common.cover.ConveyorCover. Moves items host<->adjacent, filter-gated.
// Base class of ItemVoidingCover (reuses filter + subscription, overrides Update).
//
// Adaptations: LDLib createUIWidget / buildAdditionalUI / getUITitle dropped
// (settings UI is a later layer); world-tool hooks (screwdriver / mallet /
// sideTips / copy/paste) dropped - see CoverBehavior; GetAdjacentItemHandler
// resolves via WorldCapability instead of Forge-cap lookup.
public class ConveyorCover : CoverBehavior, IIOCover, IUICover, IControllable
{
	// Verbatim CONVEYOR_SCALING: 8 32 128 512 1024 ... = 2 x 4^min(tier, LuV).
	public static int ConveyorScaling(int tier) =>
		2 * (int)Math.Pow(4, Math.Min(tier, (int)VoltageTier.LuV));

	public readonly int Tier;
	public readonly int MaxItemTransferRate;

	public int TransferRate { get; protected set; }
	public IO Io { get; protected set; }
	public DistributionMode DistributionMode { get; protected set; }
	public ManualIOMode ManualIOMode { get; protected set; } = ManualIOMode.Disabled;

	protected bool _isWorkingEnabled = true;
	protected int _itemsLeftToTransferLastSecond;

	protected readonly ItemFilterHandler FilterHandler;
	protected readonly ConditionalSubscriptionHandler SubscriptionHandler;

	// Exposes the filter handler to the settings popup's install slot +
	// 3x3 phantom matcher. Inherited by ItemVoidingCover / Advanced*.
	public override ItemFilterHandler? UiItemFilterHandler => FilterHandler;

	public ConveyorCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide,
		int tier, int maxTransferRate)
		: base(definition, coverHolder, attachedSide)
	{
		Tier = tier;
		MaxItemTransferRate = maxTransferRate;
		TransferRate = maxTransferRate;
		_itemsLeftToTransferLastSecond = TransferRate;
		Io = IO.OUT;
		DistributionMode = DistributionMode.InsertFirst;

		SubscriptionHandler = new ConditionalSubscriptionHandler(coverHolder, Update, IsSubscriptionActive);
		FilterHandler = FilterHandlers.Item(this);
		FilterHandler.WithFilterLoaded(ConfigureFilter)
			.WithFilterUpdated(ConfigureFilter)
			.WithFilterRemoved(ConfigureFilter);
	}

	public ConveyorCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide, int tier)
		: this(definition, coverHolder, attachedSide, tier, ConveyorScaling(tier)) { }

	protected virtual bool IsSubscriptionActive() =>
		IsWorkingEnabled() && GetAdjacentItemHandler() != null;

	// Verbatim upstream coverHolder.getCapability(ITEM_HANDLER, attachedSide).
	// Machines: io-gated, NOT cover-wrapped (a cover mustn't re-apply itself to
	// its own host read). Pipes: the ItemNetHandler.
	protected IItemHandler? GetOwnItemHandler()
	{
		var side = WorldCapability.ToIODirection(AttachedSide);
		if (CoverHolder is MetaMachine m)
			return m.GetItemHandlerCap(side, useCoverCapability: false);
		if (CoverHolder is TerrariaCompat.Pipelike.PipeCoverable pcv)
			return WorldCapability.ItemHandlerAt(pcv.X, pcv.Y, side);
		return null;
	}

	// Equivalent of upstream's Forge ITEM_HANDLER capability lookup
	// at pos.relative(side), via WorldCapability.
	protected IItemHandler? GetAdjacentItemHandler()
	{
		var dir = WorldCapability.ToIODirection(AttachedSide);

		if (CoverHolder is MetaMachine machine)
		{
			var own = GetOwnItemHandler();
			foreach (var (side, x, y) in WorldCapability.Perimeter(machine))
			{
				if (side != dir) continue;
				// Cover-aware neighbour resolution - its per-side covers +
				// output-side gating apply. Arrival side = neighbour's opposite.
				var handler = WorldCapability.ItemHandlerAt(x, y, side.Opposite());
				if (handler != null && !ReferenceEquals(handler, own))
					return handler;
			}
			return null;
		}

		if (CoverHolder is TerrariaCompat.Pipelike.PipeCoverable pcv)
		{
			// PipeNeighborProbe is the single source of AIR / PIPE / INVENTORY
			// exclusivity. "Pipe" requires same-net connection, so different-
			// material pipes don't block each other's inventory connections.
			var kind = TerrariaCompat.Pipelike.PipeNeighborProbe.ProbeAt(
				pcv.X, pcv.Y, AttachedSide, pcv.Layer);
			if (kind == TerrariaCompat.Pipelike.SideNeighbourKind.Pipe) return null;
			if (kind == TerrariaCompat.Pipelike.SideNeighbourKind.None) return null;
			var (dx, dy) = dir.Offset();
			return WorldCapability.ItemHandlerAt(pcv.X + dx, pcv.Y + dy, dir.Opposite());
		}
		return null;
	}

	public void SetDistributionMode(DistributionMode mode) => DistributionMode = mode;

	// Upstream-faithful: gate on `coverHolder.getCapability(ITEM_HANDLER, side)`.
	public override bool CanAttach() => base.CanAttach() && GetOwnItemHandler() != null;

	public void SetTransferRate(int transferRate)
	{
		if (transferRate <= MaxItemTransferRate) TransferRate = transferRate;
	}

	public void SetIo(IO io)
	{
		if (io is IO.IN or IO.OUT) Io = io;
		SubscriptionHandler.UpdateSubscription();
	}

	protected void SetManualIOMode(ManualIOMode manualIOMode) => ManualIOMode = manualIOMode;

	public override void OnLoad()
	{
		base.OnLoad();
		SubscriptionHandler.Initialize();
	}

	public override void OnRemoved()
	{
		base.OnRemoved();
		SubscriptionHandler.Unsubscribe();
	}

	public override List<Item> GetAdditionalDrops()
	{
		var list = base.GetAdditionalDrops();
		if (!FilterHandler.FilterItem.IsAir) list.Add(FilterHandler.FilterItem);
		return list;
	}

	public override void OnNeighborChanged() => SubscriptionHandler.UpdateSubscription();

	// === IControllable ======================================================

	public bool IsWorkingEnabled() => _isWorkingEnabled;

	public void SetWorkingEnabled(bool isWorkingAllowed)
	{
		if (_isWorkingEnabled != isWorkingAllowed)
		{
			_isWorkingEnabled = isWorkingAllowed;
			SubscriptionHandler.UpdateSubscription();
		}
	}

	// field 1=IO, 2=manual-IO mode, 3=transfer rate. Field 0 (working-enabled)
	// falls through to base. DistributionMode hidden upstream w/o pipe routing.
	public override void ApplySetting(int field, long value)
	{
		switch (field)
		{
			case 1: SetIo((IO)value); break;
			case 2: SetManualIOMode((ManualIOMode)System.Math.Clamp(value, 0, 2)); break;
			case 3: SetTransferRate((int)value); break;
			default: base.ApplySetting(field, value); break;
		}
	}

	protected virtual void Update()
	{
		// DEVIATION (see PipeCoverable): upstream allows one cover
		// per pipe side; we keep both filterCover AND robotArm subscribed and
		// gate the transfer body on identity here. Always-true for machines.
		if (!ReferenceEquals(((ICoverable)CoverHolder).GetCoverAtSide(AttachedSide), this))
			return;

		// Verbatim cadence: 5 MC ticks/cycle, 20 MC ticks rate reset.
		// FromMcTicks applies Terraria scale + SimulationSpeed.
		long timer = CoverHolder.GetOffsetTimer();
		int cyclePeriod = Api.TickScale.FromMcTicks(5);
		int resetPeriod = Api.TickScale.FromMcTicks(20);
		if (timer % cyclePeriod == 0)
		{
			if (_itemsLeftToTransferLastSecond > 0)
			{
				var adjacent = GetAdjacentItemHandler();
				var self = GetOwnItemHandler();
				if (adjacent != null && self != null)
				{
					int totalTransferred = Io switch
					{
						IO.IN => DoTransferItems(adjacent, self, _itemsLeftToTransferLastSecond),
						IO.OUT => DoTransferItems(self, adjacent, _itemsLeftToTransferLastSecond),
						_ => 0,
					};
					_itemsLeftToTransferLastSecond -= totalTransferred;
				}
			}
			if (timer % resetPeriod == 0)
				_itemsLeftToTransferLastSecond = TransferRate;
			SubscriptionHandler.UpdateSubscription();
		}
	}

	// RobotArmCover overrides to dispatch on TransferMode.
	protected virtual int DoTransferItems(IItemHandler sourceInventory, IItemHandler targetInventory, int maxTransferAmount) =>
		MoveInventoryItems(sourceInventory, targetInventory, maxTransferAmount);

	protected int MoveInventoryItems(IItemHandler sourceInventory, IItemHandler targetInventory, int maxTransferAmount)
	{
		var filter = FilterHandler.GetFilter();
		int itemsLeftToTransfer = maxTransferAmount;

		for (int srcIndex = 0; srcIndex < sourceInventory.SlotCount; srcIndex++)
		{
			Item sourceStack = sourceInventory.Extract(srcIndex, itemsLeftToTransfer, true);
			if (sourceStack.IsAir) continue;
			if (!filter.Test(sourceStack)) continue;

			Item remainder = InsertItem(targetInventory, sourceStack, true);
			int amountToInsert = sourceStack.stack - remainder.stack;

			if (amountToInsert > 0)
			{
				sourceStack = sourceInventory.Extract(srcIndex, amountToInsert, false);
				if (!sourceStack.IsAir)
				{
					InsertItem(targetInventory, sourceStack, false);
					itemsLeftToTransfer -= sourceStack.stack;
					if (itemsLeftToTransfer == 0) break;
				}
			}
		}
		return maxTransferAmount - itemsLeftToTransfer;
	}

	// Verbatim ItemHandlerHelper.insertItem.
	protected static Item InsertItem(IItemHandler dest, Item stack, bool simulate)
	{
		if (stack.IsAir) return new Item();
		Item remaining = stack.Clone();
		for (int i = 0; i < dest.SlotCount; i++)
		{
			remaining = dest.Insert(i, remaining, simulate);
			if (remaining.IsAir) return new Item();
		}
		return remaining;
	}

	// Keyed by type - upstream uses ItemStack comparingAllButCount; Terraria
	// item matching is type-only (see SimpleItemFilter).
	protected Dictionary<int, TypeItemInfo> CountInventoryItemsByType(IItemHandler inventory)
	{
		var filter = FilterHandler.GetFilter();
		var result = new Dictionary<int, TypeItemInfo>();

		for (int srcIndex = 0; srcIndex < inventory.SlotCount; srcIndex++)
		{
			Item itemStack = inventory.GetSlot(srcIndex);
			if (itemStack.IsAir || !filter.Test(itemStack)) continue;

			if (!result.TryGetValue(itemStack.type, out var itemInfo))
			{
				itemInfo = new TypeItemInfo(itemStack, new List<int>(), 0);
				result[itemStack.type] = itemInfo;
			}
			itemInfo.TotalCount += itemStack.stack;
			itemInfo.Slots.Add(srcIndex);
		}
		return result;
	}

	protected sealed class TypeItemInfo
	{
		public readonly Item ItemStack;
		public readonly List<int> Slots;
		public int TotalCount;

		public TypeItemInfo(Item itemStack, List<int> slots, int totalCount)
		{
			ItemStack = itemStack;
			Slots = slots;
			TotalCount = totalCount;
		}
	}

	// Count-exact transfer primitives - verbatim port of upstream
	// moveInventoryItemsExact / countInventoryItemsByMatchSlot / the
	// GroupItemInfo overload of moveInventoryItems. Per-type maps key by
	// item type (vs upstream's ItemStack hash).

	// All-or-nothing exact move of itemInfo.TotalCount across itemInfo.Slots.
	protected static bool MoveInventoryItemsExact(IItemHandler sourceInventory, IItemHandler targetInventory,
		TypeItemInfo itemInfo)
	{
		Item resultStack = itemInfo.ItemStack.Clone();
		int totalExtractedCount = 0;
		int itemsLeftToExtract = itemInfo.TotalCount;

		foreach (int slotIndex in itemInfo.Slots)
		{
			Item extracted = sourceInventory.Extract(slotIndex, itemsLeftToExtract, simulate: true);
			if (!extracted.IsAir && extracted.type == resultStack.type)
			{
				totalExtractedCount += extracted.stack;
				itemsLeftToExtract  -= extracted.stack;
			}
			if (itemsLeftToExtract == 0) break;
		}
		if (totalExtractedCount != itemInfo.TotalCount) return false;     // can't fully extract
		resultStack.stack = totalExtractedCount;
		if (!InsertItem(targetInventory, resultStack, simulate: true).IsAir) return false; // can't fully insert

		// Commit.
		InsertItem(targetInventory, resultStack, simulate: false);
		itemsLeftToExtract = itemInfo.TotalCount;
		foreach (int slotIndex in itemInfo.Slots)
		{
			Item extracted = sourceInventory.Extract(slotIndex, itemsLeftToExtract, simulate: false);
			if (!extracted.IsAir && extracted.type == resultStack.type)
				itemsLeftToExtract -= extracted.stack;
			if (itemsLeftToExtract == 0) break;
		}
		return true;
	}

	// Grouped move: each source slot drains down to its group's TotalCount,
	// capped by maxTransfer.
	protected int MoveInventoryItems(IItemHandler sourceInventory, IItemHandler targetInventory,
		Dictionary<int, GroupItemInfo> itemInfos, int maxTransferAmount)
	{
		var filter = FilterHandler.GetFilter();
		int itemsLeftToTransfer = maxTransferAmount;

		for (int i = 0; i < sourceInventory.SlotCount; i++)
		{
			Item itemStack = sourceInventory.GetSlot(i);
			if (itemStack.IsAir || !filter.Test(itemStack) || !itemInfos.ContainsKey(itemStack.type))
				continue;

			var itemInfo = itemInfos[itemStack.type];
			Item extracted = sourceInventory.Extract(i,
				System.Math.Min(itemInfo.TotalCount, itemsLeftToTransfer), simulate: true);

			Item remainder = InsertItemStacked(targetInventory, extracted, simulate: true);
			int amountToInsert = extracted.stack - remainder.stack;
			if (amountToInsert <= 0) continue;

			extracted = sourceInventory.Extract(i, amountToInsert, simulate: false);
			if (extracted.IsAir) continue;

			InsertItemStacked(targetInventory, extracted, simulate: false);
			itemsLeftToTransfer -= extracted.stack;
			itemInfo.TotalCount -= extracted.stack;

			if (itemInfo.TotalCount == 0)
			{
				itemInfos.Remove(itemStack.type);
				if (itemInfos.Count == 0) break;
			}
			if (itemsLeftToTransfer == 0) break;
		}
		return maxTransferAmount - itemsLeftToTransfer;
	}

	// Like CountInventoryItemsByType but no per-slot list.
	protected Dictionary<int, GroupItemInfo> CountInventoryItemsByMatchSlot(IItemHandler inventory)
	{
		var filter = FilterHandler.GetFilter();
		var result = new Dictionary<int, GroupItemInfo>();

		for (int srcIndex = 0; srcIndex < inventory.SlotCount; srcIndex++)
		{
			Item itemStack = inventory.GetSlot(srcIndex);
			if (itemStack.IsAir || !filter.Test(itemStack)) continue;

			if (!result.TryGetValue(itemStack.type, out var itemInfo))
				result[itemStack.type] = itemInfo = new GroupItemInfo(itemStack, 0);
			itemInfo.TotalCount += itemStack.stack;
		}
		return result;
	}

	// Shared with ItemNetHandler.
	protected static Item InsertItemStacked(IItemHandler dest, Item stack, bool simulate) =>
		Api.Capability.ItemHandlerHelper.InsertItemStacked(dest, stack, simulate);

	protected sealed class GroupItemInfo
	{
		public readonly Item ItemStack;
		public int TotalCount;

		public GroupItemInfo(Item itemStack, int totalCount)
		{
			ItemStack = itemStack;
			TotalCount = totalCount;
		}
	}

	// AdvancedItemVoidingCover overrides to clamp the filter's max stack.
	protected virtual void ConfigureFilter() { }

	// Capability override - pipe-consulted.

	private CoverableItemHandlerWrapper? _itemHandlerWrapper;

	public override IItemHandler? GetItemHandlerCap(IItemHandler? defaultValue)
	{
		if (defaultValue == null) return null;
		if (_itemHandlerWrapper == null || _itemHandlerWrapper.Inner != defaultValue)
			_itemHandlerWrapper = new CoverableItemHandlerWrapper(this, defaultValue);
		return _itemHandlerWrapper;
	}

	private sealed class CoverableItemHandlerWrapper : ItemHandlerDelegate
	{
		private readonly ConveyorCover _cover;

		public CoverableItemHandlerWrapper(ConveyorCover cover, IItemHandler inner) : base(inner) => _cover = cover;

		public override Item Insert(int slot, Item stack, bool simulate)
		{
			if (_cover.Io == IO.OUT)
			{
				if (_cover.ManualIOMode == ManualIOMode.Disabled) return stack;
				if (_cover.ManualIOMode == ManualIOMode.Unfiltered) return base.Insert(slot, stack, simulate);
			}
			if (!_cover.FilterHandler.Test(stack)) return stack;
			return base.Insert(slot, stack, simulate);
		}

		public override Item Extract(int slot, int amount, bool simulate)
		{
			if (_cover.Io == IO.IN)
			{
				if (_cover.ManualIOMode == ManualIOMode.Disabled) return new Item();
				if (_cover.ManualIOMode == ManualIOMode.Unfiltered) return base.Extract(slot, amount, simulate);
			}
			Item result = base.Extract(slot, amount, true);
			if (result.IsAir || !_cover.FilterHandler.Test(result)) return new Item();
			return simulate ? result : base.Extract(slot, amount, false);
		}
	}

	// === Persistence ========================================================

	public override void Save(TagCompound tag)
	{
		base.Save(tag);
		tag["transferRate"] = TransferRate;
		tag["io"] = (int)Io;
		tag["distributionMode"] = (int)DistributionMode;
		tag["manualIO"] = (int)ManualIOMode;
		tag["workingEnabled"] = _isWorkingEnabled;
		var filterTag = new TagCompound();
		FilterHandler.Save(filterTag);
		tag["filter"] = filterTag;
	}

	public override void Load(TagCompound tag)
	{
		base.Load(tag);
		if (tag.ContainsKey("transferRate")) TransferRate = tag.GetInt("transferRate");
		if (tag.ContainsKey("io")) Io = (IO)tag.GetInt("io");
		if (tag.ContainsKey("distributionMode")) DistributionMode = (DistributionMode)tag.GetInt("distributionMode");
		if (tag.ContainsKey("manualIO")) ManualIOMode = (ManualIOMode)tag.GetInt("manualIO");
		if (tag.ContainsKey("workingEnabled")) _isWorkingEnabled = tag.GetBool("workingEnabled");
		if (tag.ContainsKey("filter")) FilterHandler.Load(tag.GetCompound("filter"));
	}
}
