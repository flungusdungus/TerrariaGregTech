#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Cover.Data;
using GregTechCEuTerraria.Api.Cover.Filter;
using GregTechCEuTerraria.Api.Machine;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Cover;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;
using Item = Terraria.Item;   // disambiguate from this file's `Item` sub-namespace

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.ItemPipe;

// Verbatim port of com.gregtechceu.gtceu.common.pipelike.item.ItemNetHandler.
// One instance per (pipe cell, source side). Insert is the pipe network's
// entry point; Extract / SetSlot no-op like upstream's IItemHandlerModifiable.
//
// Adaptations: ItemPipeBlockEntity -> PipeCoverable, Direction -> CoverSide,
// ItemStack -> Item, pipe.isBlocked(facing) -> cover-at-side == null.
public sealed class ItemNetHandler : IItemHandler
{
	public ItemPipeNet Network { get; set; }
	private readonly PipeCoverable _pipe;
	public CoverSide Facing { get; }

	private int _simulatedTransfers;
	private readonly Dictionary<(int x, int y, CoverSide f), int> _simulatedTransfersGlobalRoundRobin = new();

	public ItemNetHandler(ItemPipeNet net, PipeCoverable pipe, CoverSide facing)
	{
		Network = net;
		_pipe = pipe;
		Facing = facing;
	}

	public int SlotCount => 1;
	public int GetSlotLimit(int slot) => 64;
	public bool IsItemValid(int slot, Item item) => true;
	public Item GetSlot(int slot) => new();
	public void SetSlot(int slot, Item item) { /* no-op */ }
	public Item Extract(int slot, int maxAmount, bool simulate) => new();

	public Item Insert(int slot, Item stack, bool simulate)
	{
		if (stack.IsAir) return stack;

		// Re-resolve every call - PipeNet merge/split replaces the per-net
		// instance, a stale Network would route through a dead net.
		Network = ItemPipeNetSystem.Level.GetNetFromPos((_pipe.X, _pipe.Y))!;

		if (Network is null || _pipe is null || !ItemPipeLayerSystem.Pipes.Has(_pipe.X, _pipe.Y) ||
			IsBlocked(_pipe, Facing))
		{
			return stack;
		}

		_simulatedTransfers = _pipe.TransferredItems;
		_simulatedTransfersGlobalRoundRobin.Clear();
		foreach (var kv in _pipe.Transferred) _simulatedTransfersGlobalRoundRobin[kv.Key] = kv.Value;

		var pipeCover = ((ICoverable)_pipe).GetCoverAtSide(Facing);
		var tileCover = GetCoverOnNeighbour(_pipe.X, _pipe.Y, Facing);
		ConveyorCover? conveyor = null;

		// Two conveyors cancel out (upstream parity).
		if (pipeCover is ConveyorCover && tileCover is ConveyorCover) return stack;

		if (!CheckImportCover(tileCover, onPipe: false, stack)) return stack;

		if (pipeCover is ConveyorCover pipeConveyor) conveyor = pipeConveyor;
		if (tileCover is ConveyorCover tileConveyor) conveyor = tileConveyor;

		var routePaths = Network.GetNetData((_pipe.X, _pipe.Y), Facing, ItemRoutePathSet.Full);
		if (routePaths.Count == 0) return stack;
		var routePathsCopy = new List<ItemRoutePath>(routePaths);

		if (conveyor is null) return DistributeHighestPriority(routePathsCopy, stack, simulate);

		switch (conveyor.DistributionMode)
		{
			case DistributionMode.InsertFirst:
				stack = DistributeHighestPriority(routePathsCopy, stack, simulate); break;
			case DistributionMode.RoundRobinGlobal:
				stack = DistributeEqually(routePathsCopy, stack, simulate); break;
			case DistributionMode.RoundRobinPrio:
				stack = DistributeEquallyNoRestrictive(stack, simulate); break;
		}
		return stack;
	}

	private Item DistributeHighestPriority(List<ItemRoutePath> copy, Item stack, bool simulate)
	{
		foreach (var inv in copy)
		{
			stack = InsertIntoTarget(inv, stack, simulate, ignoreLimit: false);
			if (stack.IsAir) return new Item();
		}
		return stack;
	}

	private Item DistributeEquallyNoRestrictive(Item stack, bool simulate)
	{
		var nonRestricted = new List<ItemRoutePath>(
			Network.GetNetData((_pipe.X, _pipe.Y), Facing, ItemRoutePathSet.NonRestricted));
		Item remainsNonRestricted = nonRestricted.Count == 0
			? stack
			: DistributeEqually(nonRestricted, stack, simulate);
		if (!remainsNonRestricted.IsAir)
		{
			var restricted = new List<ItemRoutePath>(
				Network.GetNetData((_pipe.X, _pipe.Y), Facing, ItemRoutePathSet.Restricted));
			return DistributeEqually(restricted, remainsNonRestricted, simulate);
		}
		return new Item();
	}

	private sealed class EnhancedRoundRobinData
	{
		public readonly ItemRoutePath RoutePath;
		public readonly int MaxInsertable;
		public int Transferred;
		public int ToTransfer;
		public EnhancedRoundRobinData(ItemRoutePath p, int maxInsertable, int transferred)
		{
			RoutePath = p; MaxInsertable = maxInsertable; Transferred = transferred;
		}
	}

	private Item DistributeEqually(List<ItemRoutePath> copy, Item stack, bool simulate)
	{
		var transferred = new List<EnhancedRoundRobinData>();
		var steps = new List<int>();
		int min = int.MaxValue;

		// Discover non-full targets + their already-transferred counts.
		foreach (var inv in copy)
		{
			var simStack = stack.Clone();
			int ins = stack.stack - InsertIntoTarget(inv, simStack, simulate: true, ignoreLimit: true).stack;
			if (ins <= 0) continue;
			int didTransfer = DidTransferTo(inv, simulate);
			transferred.Add(new EnhancedRoundRobinData(inv, ins, didTransfer));
			if (didTransfer < min) min = didTransfer;
			if (!steps.Contains(didTransfer)) steps.Add(didTransfer);
		}

		if (transferred.Count == 0 || steps.Count == 0) return stack;

		if (!simulate && min < int.MaxValue) DecrementBy(min);

		transferred.Sort((a, b) => a.Transferred.CompareTo(b.Transferred));
		steps.Sort();

		if (transferred[0].Transferred != steps[0]) return stack;

		int amount = stack.stack;
		int c = amount / transferred.Count;
		int m = amount % transferred.Count;
		var transferredCopy = new List<EnhancedRoundRobinData>(transferred);
		int nextStep = steps[0];
		steps.RemoveAt(0);

		// Forward iteration with mid-loop removal mirrors upstream's
		// Iterator.remove() semantics; order determines who gets leftover items.
		bool brokenOuter = false;
		while (amount > 0 && transferredCopy.Count > 0 && !brokenOuter)
		{
			int i = 0;
			while (i < transferredCopy.Count)
			{
				var data = transferredCopy[i];
				if (nextStep >= 0 && data.Transferred >= nextStep) break;

				int toInsert;
				if (nextStep <= 0)
				{
					if (amount <= m) toInsert = 1;
					else             toInsert = System.Math.Min(c, amount);
				}
				else
				{
					toInsert = System.Math.Min(amount, nextStep - data.Transferred);
				}
				if (data.ToTransfer + toInsert >= data.MaxInsertable)
				{
					data.ToTransfer = data.MaxInsertable;
					transferredCopy.RemoveAt(i);   // stay at i; removal shifts the next entry here
				}
				else
				{
					data.ToTransfer += toInsert;
					i++;
				}
				data.Transferred += toInsert;
				amount -= toInsert;
				if (amount == 0) { brokenOuter = true; break; }
			}
			if (brokenOuter) break;

			// Upstream's `continue outer` if any target hasn't reached nextStep.
			bool allAtStep = true;
			foreach (var data in transferredCopy)
				if (data.Transferred < nextStep) { allAtStep = false; break; }
			if (!allAtStep) continue;

			if (steps.Count == 0)
			{
				if (nextStep >= 0)
				{
					c = transferredCopy.Count == 0 ? 0 : amount / transferredCopy.Count;
					m = transferredCopy.Count == 0 ? 0 : amount % transferredCopy.Count;
					nextStep = -1;
				}
			}
			else
			{
				nextStep = steps[0];
				steps.RemoveAt(0);
			}
		}

		int inserted = 0;
		foreach (var data in transferred)
		{
			var toInsert = stack.Clone();
			toInsert.stack = data.ToTransfer;
			int ins = data.ToTransfer - InsertIntoTarget(data.RoutePath, toInsert, simulate, ignoreLimit: false).stack;
			inserted += ins;
			TransferTo(data.RoutePath, simulate, ins);
		}

		var remainder = stack.Clone();
		remainder.stack -= inserted;
		if (remainder.stack <= 0) remainder.TurnToAir();
		return remainder;
	}

	private Item InsertIntoTarget(ItemRoutePath routePath, Item stack, bool simulate, bool ignoreLimit)
	{
		int allowed = ignoreLimit
			? stack.stack
			: CheckTransferable(routePath.Properties.TransferRate, stack.stack, simulate);
		if (allowed == 0 || !routePath.MatchesFilters(stack)) return stack;

		var targetPipeCov = ItemPipeLayerSystem.GetSides(routePath.TargetPipe.X, routePath.TargetPipe.Y);
		var pipeCover = targetPipeCov?.GetCoverAtSide(routePath.TargetFacing);
		var tileCover = GetCoverOnNeighbour(routePath.TargetPipe.X, routePath.TargetPipe.Y, routePath.TargetFacing);

		if (pipeCover != null)
		{
			// Probe through the cover; Extract==0 means cover is vetoing.
			// Default FilterInsert+AllowFlow=Disabled blocks until player
			// flips AllowFlow / FilterMode (upstream-verbatim).
			var defaultHandler = new ProbeHandler(stack.Clone());
			IItemHandler? itemHandler = pipeCover.GetItemHandlerCap(defaultHandler);
			if (itemHandler is null) return stack;
			if (!ReferenceEquals(itemHandler, defaultHandler))
			{
				int extracted = itemHandler.Extract(0, allowed, simulate: true).stack;
				if (extracted <= 0) return stack;
				allowed = extracted;
			}
		}

		var neighbourHandler = routePath.GetHandler();
		if (neighbourHandler is null) return stack;

		if (pipeCover is RobotArmCover armOut && armOut.Io == IO.OUT)
			return InsertOverRobotArm(neighbourHandler, armOut, stack, simulate, allowed, ignoreLimit);
		if (tileCover is RobotArmCover armIn && armIn.Io == IO.IN)
			return InsertOverRobotArm(neighbourHandler, armIn, stack, simulate, allowed, ignoreLimit);

		return InsertIntoDestination(neighbourHandler, stack, simulate, allowed, ignoreLimit);
	}

	private Item InsertIntoDestination(IItemHandler handler, Item stack, bool simulate, int allowed, bool ignoreLimit)
	{
		if (stack.stack == allowed)
		{
			Item re = ItemHandlerHelper.InsertItemStacked(handler, stack, simulate);
			if (!ignoreLimit) Transfer(simulate, stack.stack - re.stack);
			return re;
		}
		var toInsert = stack.Clone();
		toInsert.stack = System.Math.Min(allowed, stack.stack);
		int r = ItemHandlerHelper.InsertItemStacked(handler, toInsert, simulate).stack;
		if (!ignoreLimit) Transfer(simulate, toInsert.stack - r);
		var remainder = stack.Clone();
		remainder.stack = r + (stack.stack - toInsert.stack);
		if (remainder.stack <= 0) remainder.TurnToAir();
		return remainder;
	}

	private Item InsertOverRobotArm(IItemHandler handler, RobotArmCover arm, Item stack,
		bool simulate, int allowed, bool ignoreLimit)
	{
		int rate = arm.GetFilterHandler().IsFilterPresent
			? (arm.GetFilterHandler().GetFilter() is IItemFilter f ? f.TestItemCount(stack) : int.MaxValue)
			: int.MaxValue;
		int count;
		switch (arm.TransferMode)
		{
			case TransferMode.TransferAny:
				return InsertIntoDestination(handler, stack, simulate, allowed, ignoreLimit);
			case TransferMode.KeepExact:
				if (rate == int.MaxValue) rate = arm.GlobalTransferLimit;
				count = rate - CountStack(handler, stack, arm);
				if (count <= 0) return stack;
				count = System.Math.Min(allowed, System.Math.Min(stack.stack, count));
				return InsertIntoDestination(handler, stack, simulate, count, ignoreLimit);
			case TransferMode.TransferExact:
				int max = allowed + arm.GetBuffer();
				count = System.Math.Min(max, System.Math.Min(rate, stack.stack));
				if (count < rate)
				{
					arm.Buffer(allowed);
					return stack;
				}
				else
				{
					arm.ClearBuffer();
				}
				if (InsertIntoDestination(handler, stack, simulate: true, count, ignoreLimit).stack != stack.stack - count)
					return stack;
				return InsertIntoDestination(handler, stack, simulate, count, ignoreLimit);
		}
		return stack;
	}

	public static int CountStack(IItemHandler handler, Item stack, RobotArmCover? arm)
	{
		if (arm is null) return 0;
		int count = 0;
		var filter = arm.GetFilterHandler().GetFilter();
		bool ignoreNbt = filter is SimpleItemFilter sif && sif.IgnoreNbt;
		for (int i = 0; i < handler.SlotCount; i++)
		{
			Item slot = handler.GetSlot(i);
			if (slot.IsAir) continue;
			// !ignoreNbt also matches prefix (closest analog to MC's NBT).
			if (ignoreNbt  && slot.type != stack.type) continue;
			if (!ignoreNbt && (slot.type != stack.type || slot.prefix != stack.prefix)) continue;
			if (filter.Test(slot)) count += slot.stack;
		}
		return count;
	}

	public static bool CheckImportCover(CoverBehavior? cover, bool onPipe, Item stack)
	{
		if (cover is ItemFilterCover filter)
		{
			return (filter.FilterMode != FilterMode.FilterBoth &&
					(filter.FilterMode != FilterMode.FilterInsert  || !onPipe) &&
					(filter.FilterMode != FilterMode.FilterExtract ||  onPipe)) ||
					filter.GetItemFilter().Test(stack);
		}
		return true;
	}

	// Upstream getCoverOnNeighbour - cover on the adjacent machine's
	// facing-back side; null if no machine / no cover.
	public static CoverBehavior? GetCoverOnNeighbour(int pipeX, int pipeY, CoverSide handlerFacing)
	{
		var (dx, dy) = OffsetForCoverSide(handlerFacing);
		int nx = pipeX + dx, ny = pipeY + dy;
		if (!MachineCellResolver.TryFindMachineAt(nx, ny, out var machine)) return null;
		return ((ICoverable)machine).GetCoverAtSide(CoverSides.Opposite(handlerFacing));
	}

	private static bool IsBlocked(PipeCoverable pipe, CoverSide side) =>
		((ICoverable)pipe).GetCoverAtSide(side) is null;

	private int CheckTransferable(float rate, int amount, bool simulate)
	{
		int max = (int)((rate * 64f) + 0.5f);
		return simulate
			? System.Math.Max(0, System.Math.Min(max - _simulatedTransfers,     amount))
			: System.Math.Max(0, System.Math.Min(max - _pipe.TransferredItems,  amount));
	}

	private void Transfer(bool simulate, int amount)
	{
		if (simulate) _simulatedTransfers += amount;
		else          _pipe.TransferredItems += amount;
	}

	private void TransferTo(ItemRoutePath route, bool simulate, int amount)
	{
		var key = route.ToFacingPos();
		if (simulate)
		{
			_simulatedTransfersGlobalRoundRobin[key] =
				(_simulatedTransfersGlobalRoundRobin.TryGetValue(key, out var v) ? v : 0) + amount;
		}
		else
		{
			_pipe.Transferred[key] = (_pipe.Transferred.TryGetValue(key, out var v) ? v : 0) + amount;
		}
	}

	private int DidTransferTo(ItemRoutePath route, bool simulate) =>
		simulate
			? (_simulatedTransfersGlobalRoundRobin.TryGetValue(route.ToFacingPos(), out var s) ? s : 0)
			: (_pipe.Transferred.TryGetValue(route.ToFacingPos(), out var v) ? v : 0);

	private void DecrementBy(int amount)
	{
		var keys = new List<(int x, int y, CoverSide f)>(_pipe.Transferred.Keys);
		foreach (var k in keys)
			_pipe.Transferred[k] -= amount;
	}

	private static (int dx, int dy) OffsetForCoverSide(CoverSide side) => side switch
	{
		CoverSide.Up    => (0, -1),
		CoverSide.Down  => (0, +1),
		CoverSide.Left  => (-1, 0),
		CoverSide.Right => (+1, 0),
		_               => (0, 0),
	};

	// Upstream pattern: `new ItemStackHandler(1)` seeded with the inserted stack.
	private sealed class ProbeHandler : IItemHandler
	{
		private Item _slot;
		public ProbeHandler(Item stack) { _slot = stack; }
		public int SlotCount => 1;
		public int GetSlotLimit(int slot) => 64;
		public bool IsItemValid(int slot, Item item) => true;
		public Item GetSlot(int slot) => _slot;
		public void SetSlot(int slot, Item item) => _slot = item;
		public Item Insert(int slot, Item item, bool simulate) => item; // probe accepts nothing
		public Item Extract(int slot, int maxAmount, bool simulate)
		{
			if (_slot.IsAir || maxAmount <= 0) return new Item();
			int take = System.Math.Min(maxAmount, _slot.stack);
			var taken = _slot.Clone();
			taken.stack = take;
			if (!simulate)
			{
				_slot.stack -= take;
				if (_slot.stack <= 0) _slot = new Item();
			}
			return taken;
		}
	}
}
