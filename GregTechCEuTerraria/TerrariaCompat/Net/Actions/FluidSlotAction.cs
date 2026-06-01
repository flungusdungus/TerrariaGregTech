#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Items.Fluids;
using Terraria;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// R-click on a UIFluidSlot with a bucket OR fluid cell on the cursor. Full
// server authority - server validates fill/drain against the authoritative
// IFluidHandler, mutates the tank, and returns the swapped cursor item via
// CursorUpdatePacket. Constrained to Main.mouseItem (cursor) as the source.
//
// Dispatch:
//   - Cursor is a vanilla bucket (WaterBucket/LavaBucket/EmptyBucket) ->
//     route via VanillaFluidBridge (1000 mB per click; type swap).
//   - Cursor is a FluidCellItem -> route via IFluidHandlerItem
//     (cell-capacity per click; NBT-mutate in place if stack=1; spawn new
//     filled/empty cell + decrement stack if stack>1).
public sealed class FluidSlotAction : IMachineAction
{
	public PacketType Type => PacketType.FluidSlotAction;

	private byte _tankIndex;
	private Item _cursor = new();

	public FluidSlotAction() { }
	public FluidSlotAction(int tankIndex, Item cursor)
	{
		_tankIndex = (byte)tankIndex;
		_cursor = cursor.Clone();
	}

	public void Write(BinaryWriter w)
	{
		w.Write(_tankIndex);
		w.WriteItem(_cursor);
	}

	public void Read(BinaryReader r)
	{
		_tankIndex = r.ReadByte();
		_cursor = r.ReadItem();
	}

	public void Apply(MetaMachine entity, int byWhoAmI)
	{
		if (entity is not IFluidHandler handler) return;
		if (_cursor.IsAir) return;
		if (_tankIndex >= handler.TankCount) return;

		// Raw, direction-free single-tank handler for the clicked tank - the
		// centralized UI-interaction path (IFluidHandler.GetTankAccess, our
		// equivalent of upstream's TankWidget binding to getStorages()[tank]).
		// All transfer below goes through this, never the machine's
		// whole-handler IO-gated Fill/Drain. Type filters on the storage are
		// still enforced; only the IO-direction gate is bypassed.
		var tank = handler.GetTankAccess(_tankIndex);

		// Server-authoritative click gate - same per-tank capability tuple the
		// UI widget reads (IFluidHandler.GetTankClickCaps), so a malicious
		// client can't bypass either direction. Mirrors upstream's TankWidget
		// (allowClickDrained, allowClickFilled) pair.
		(bool allowFill, bool allowDrain) = handler.GetTankClickCaps(_tankIndex);

		// === FluidBucketItem path - per-fluid filled bucket ================
		// Fills the tank with one full bucket; the bucket empties to a vanilla
		// EmptyBucket (which round-trips back via the empty-bucket path below).
		if (_cursor.ModItem is FluidBucketItem gtBucket && gtBucket.Fluid is { } bucketFluidType)
		{
			if (!allowFill) return;
			var stack = new FluidStack(bucketFluidType, VanillaFluidBridge.BucketAmount);
			if (tank.Fill(stack, simulate: true) < VanillaFluidBridge.BucketAmount)
				return; // not enough room for a full bucket
			tank.Fill(stack, simulate: false);
			SwapOneFromCursorByType(ItemID.EmptyBucket);
			DeliverCursor(byWhoAmI);
			return;
		}

		// === FluidCellItem path (NBT-discriminated empty/filled) ===========
		if (_cursor.ModItem is FluidCellItem cell)
		{
			if (cell.GetFluidStack().IsEmpty)
			{
				if (!allowDrain) return;
				ApplyFillCellFromTank(tank, cell);
			}
			else
			{
				if (!allowFill) return;
				ApplyDrainCellIntoTank(tank, cell);
			}
			DeliverCursor(byWhoAmI);
			return;
		}

		// === Vanilla bucket path ============================================
		var bucketFluid = VanillaFluidBridge.StackFor(_cursor.type);
		if (!bucketFluid.IsEmpty)
		{
			if (!allowFill) return;
			int accepted = tank.Fill(bucketFluid, simulate: true);
			if (accepted < bucketFluid.Amount) return; // not enough room for a full bucket
			tank.Fill(bucketFluid, simulate: false);
			int emptyType = VanillaFluidBridge.EmptyVersion(_cursor.type);
			SwapOneFromCursorByType(emptyType);
			DeliverCursor(byWhoAmI);
			return;
		}
		if (_cursor.type == ItemID.EmptyBucket)
		{
			if (!allowDrain) return;
			var stored = tank.GetTank(0);
			if (stored.IsEmpty) return;
			// Water / lava drain into their vanilla filled bucket; every other
			// fluid drains into its GT per-fluid bucket.
			int filledType = VanillaFluidBridge.FilledVersion(_cursor.type, stored.Type!);
			if (filledType == 0)
				filledType = FluidBucketRegistry.Get(stored.Type!.Id) ?? 0;
			if (filledType == 0) return;
			// Simulate first - only hand over a filled bucket if the tank
			// actually yields a full one.
			if (tank.Drain(VanillaFluidBridge.BucketAmount, simulate: true).Amount
			    < VanillaFluidBridge.BucketAmount)
				return;
			tank.Drain(VanillaFluidBridge.BucketAmount, simulate: false);
			SwapOneFromCursorByType(filledType);
			DeliverCursor(byWhoAmI);
		}
	}

	// === Cell fill: tank -> empty cell ===
	// `tank` is the raw single-tank handler from GetTankAccess.
	private void ApplyFillCellFromTank(IFluidHandler tank, FluidCellItem cell)
	{
		var tankStack = tank.GetTank(0);
		if (tankStack.IsEmpty) return;

		// Drain up to cell capacity. Cells require a full transfer of what
		// the tank can spare - partial fills are allowed (upstream matches).
		int wantAmount = cell.Capacity;
		var simDrained = tank.Drain(wantAmount, simulate: true);
		if (simDrained.IsEmpty) return;
		tank.Drain(simDrained.Amount, simulate: false);

		if (_cursor.stack == 1)
		{
			// Mutate the cursor cell's NBT in place.
			cell.Fill(simDrained, simulate: false);
		}
		else
		{
			// Stack > 1: decrement and spawn a new filled cell of the same
			// type into the player's inventory via the pending-delivery slot.
			_cursor.stack -= 1;
			_extraDelivery = MakeFilledCell(_cursor.type, simDrained);
		}
	}

	// === Cell drain: filled cell -> tank ===
	// `tank` is the raw single-tank handler from GetTankAccess.
	private void ApplyDrainCellIntoTank(IFluidHandler tank, FluidCellItem cell)
	{
		var cellStack = cell.GetFluidStack();
		if (cellStack.IsEmpty) return;
		int accepted = tank.Fill(cellStack, simulate: true);
		if (accepted <= 0) return;

		tank.Fill(cellStack.WithAmount(accepted), simulate: false);

		if (_cursor.stack == 1)
		{
			// Drain the cursor cell's NBT in place. If tank couldn't take all
			// of it, the cell keeps the remainder.
			cell.Drain(accepted, simulate: false);
		}
		else
		{
			// Stack > 1: decrement and spawn either an empty cell (full
			// transfer) or a partially-drained cell (partial transfer).
			_cursor.stack -= 1;
			if (accepted < cellStack.Amount)
				_extraDelivery = MakeFilledCell(_cursor.type, cellStack.WithAmount(cellStack.Amount - accepted));
			else
				_extraDelivery = MakeEmptyCell(_cursor.type);
		}
	}

	// === Cell construction helpers ===
	private static Item MakeFilledCell(int cellItemType, FluidStack contents)
	{
		var item = new Item();
		item.SetDefaults(cellItemType);
		if (item.ModItem is FluidCellItem c)
			c.Fill(contents, simulate: false);
		return item;
	}

	private static Item MakeEmptyCell(int cellItemType)
	{
		var item = new Item();
		item.SetDefaults(cellItemType);
		return item;
	}

	// === Vanilla bucket helpers ===

	// Mirror of UIFluidSlot.SwapHeldStack - replace ONE bucket out of the
	// cursor stack with the swap type. Stack>1: drop the rest server-side
	// (the non-swapped buckets go to the player's inventory via
	// DeliverCursor's extra-delivery path).
	private void SwapOneFromCursorByType(int swapToType)
	{
		if (swapToType <= 0) return;
		if (_cursor.stack == 1)
		{
			_cursor.SetDefaults(swapToType);
		}
		else
		{
			_cursor.stack -= 1;
			_extraDelivery = new Item();
			_extraDelivery.SetDefaults(swapToType);
		}
	}

	// Pending delivery item for the stack>1 case - held until Apply finishes
	// so we can address it at the right player.
	private Item? _extraDelivery;

	private void DeliverCursor(int byWhoAmI)
	{
		if (Main.netMode == NetmodeID.Server)
		{
			CursorUpdatePacket.SendTo(byWhoAmI, _cursor, CursorUpdatePacket.Delivery.Cursor);
			if (_extraDelivery is { IsAir: false } extra)
				CursorUpdatePacket.SendTo(byWhoAmI, extra, CursorUpdatePacket.Delivery.PlayerInventory);
			return;
		}
		// SinglePlayer - write back in-process.
		Main.mouseItem = _cursor;
		if (_extraDelivery is { IsAir: false } e)
		{
			var leftover = Main.LocalPlayer.GetItem(
				Main.myPlayer, e,
				Terraria.GetItemSettings.InventoryEntityToPlayerInventorySettings);
			if (!leftover.IsAir && leftover.stack > 0)
				Item.NewItem(new Terraria.DataStructures.EntitySource_Misc("gtceu_bucket_overflow"),
					Main.LocalPlayer.position, Main.LocalPlayer.width, Main.LocalPlayer.height,
					leftover.type, leftover.stack, false, leftover.prefix);
		}
	}
}
