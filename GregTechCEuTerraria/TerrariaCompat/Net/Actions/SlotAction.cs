#nullable enable
using System.IO;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// Click on a UISlot. Full server authority - the client never reports the
// post-click slot state, only its intent and the cursor it claims to be
// holding. Server resolves the swap on its authoritative slot array and
// sends the new cursor back via CursorUpdatePacket.
//
// This mirrors MagicStorage's TEStorageHeart.Operation pattern: client says
// "I want to do X on slot Y holding cursor Z", server is the only place that
// decides what Y ends up looking like and what the client's cursor becomes.
// Auto-output, recipe completion landing new outputs, and pipe insertion all
// mutate the same authoritative slot array on the server tick - they
// serialize naturally with the action because both run server-side, so the
// race that breaks client-snapshot designs (slot mutated between client's
// read and the server processing the snapshot) cannot produce a dupe here.
//
// In SP, MachineActions.Send calls Apply in-process with `Main.mouseItem` as
// the claimed cursor; Apply mutates Main.mouseItem directly and there is no
// CursorUpdatePacket round-trip.
public sealed class SlotAction : IMachineAction
{
	public enum Kind : byte
	{
		Left          = 0,   // L-click - stack-merge, swap, pickup, place
		Right         = 1,   // R-click - half-pickup, single-deposit
		ShiftClickOut = 2,   // Shift+L-click on slot - send full stack to player inv
		ShiftClickIn  = 3,   // Shift+L-click in player inv - send full stack into
		                     //   machine. _cursor carries the source slot snapshot;
		                     //   _group + _index are unused (server scans every
		                     //   accepting slot via IItemHandler).
	}

	public PacketType Type => PacketType.SlotAction;

	private SlotGroup _group;
	private byte _index;
	private Kind _kind;
	private Item _cursor = new();

	public SlotAction() { }
	public SlotAction(SlotGroup group, int index, Kind kind, Item cursor)
	{
		_group = group;
		_index = (byte)index;
		_kind = kind;
		_cursor = cursor.Clone(); // detach from Main.mouseItem so we don't double-write
	}

	public void Write(BinaryWriter w)
	{
		w.Write((byte)_group);
		w.Write(_index);
		w.Write((byte)_kind);
		w.WriteItem(_cursor);
	}

	public void Read(BinaryReader r)
	{
		_group = (SlotGroup)r.ReadByte();
		_index = r.ReadByte();
		_kind = (Kind)r.ReadByte();
		_cursor = r.ReadItem();
	}

	public void Apply(MetaMachine entity, int byWhoAmI)
	{
		// Object Holder is locked while its research recipe runs - reject every
		// slot mutation server-side. Upstream enforces this in the GUI via
		// BlockableSlotWidget.setIsBlocked(isLocked); our UI ref-writes bypass the
		// handler's extract gate, and a desynced/malicious client must not be able
		// to pull the orb mid-recipe, so the authority check lives here too. Echo
		// the claimed cursor straight back so the client's optimistic UI settles.
		if (entity is global::GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part.ObjectHolderMachine holder && holder.IsLocked)
		{
			if (_kind == Kind.ShiftClickIn)
				WriteBackCursor(byWhoAmI, _cursor, CursorUpdatePacket.Delivery.PlayerInventory);
			else if (_kind != Kind.ShiftClickOut)
				WriteBackCursor(byWhoAmI, _cursor, CursorUpdatePacket.Delivery.Cursor);
			return;
		}

		// ShiftClickIn has no per-slot target - server scans every accepting
		// slot via the machine's IItemHandler. Dispatch before the slot-group
		// lookup so an unused _index doesn't trip the bounds check below.
		if (_kind == Kind.ShiftClickIn)
		{
			ApplyShiftIn(entity, byWhoAmI);
			return;
		}

		var slots = entity.GetSlotGroup(_group);
		if (slots is null || _index >= slots.Length) return;

		// Server-authoritative output-slot gate - output slots refuse deposit
		// (held-cursor) L-click and R-click. Mirrors upstream SlotWidget's
		// canPutItems=false; the client UI shares this rule but the server
		// re-checks so a malicious client can't dump items into a result
		// slot. Shift-out (extraction) and empty-cursor pickup paths are
		// unaffected. On rejection we still echo the unchanged cursor back so
		// the client's optimistic UI settles on the right state.
		bool depositOnOutput = _group == SlotGroup.InventoryOutput && !_cursor.IsAir
			&& (_kind == Kind.Left || _kind == Kind.Right);

		if (!depositOnOutput)
		{
			switch (_kind)
			{
				case Kind.Left:          ApplyLeftClick(slots);  break;
				case Kind.Right:         ApplyRightClick(slots); break;
				case Kind.ShiftClickOut: ApplyShiftOut(slots, byWhoAmI); break; // owns its own cursor delivery
			}
		}

		// The ref-writes above mutate the trait backing array directly, which
		// bypasses the handler's OnContentsChanged. Notify so content-change
		// listeners run - most importantly, the RecipeLogic wake (a machine
		// that went idle re-searches once inputs land).
		entity.NotifySlotGroupChanged(_group);

		// Common cursor-result delivery for L/R:
		//   - SP: write back to Main.mouseItem in-process (we ARE the client).
		//   - Server: send CursorUpdatePacket to the originating player.
		// ShiftClickOut owns its own cursor delivery inside ApplyShiftOut.
		if (_kind != Kind.ShiftClickOut)
			WriteBackCursor(byWhoAmI, _cursor, CursorUpdatePacket.Delivery.Cursor);
	}

	// Vanilla L-click semantics, reimplemented server-side because vanilla
	// ItemSlot.LeftClick reads/writes Main.mouseItem which is client-only.
	// Covers the four cases: empty/empty, empty/has, has/empty, has/has
	// (stack-merge same type, swap different type). Favorite/prefix flags
	// ride along via Item.Clone.
	private void ApplyLeftClick(Item[] slots)
	{
		ref Item slot = ref slots[_index];
		if (slot.IsAir && _cursor.IsAir) return;
		if (slot.IsAir) { slot = _cursor; _cursor = new Item(); return; }
		if (_cursor.IsAir) { _cursor = slot; slot = new Item(); return; }
		if (CanStack(slot, _cursor))
		{
			int room = slot.maxStack - slot.stack;
			int moved = System.Math.Min(room, _cursor.stack);
			slot.stack += moved;
			_cursor.stack -= moved;
			if (_cursor.stack <= 0) _cursor = new Item();
			return;
		}
		// Different type or unstackable - swap.
		(slot, _cursor) = (_cursor, slot);
	}

	// R-click semantics: pick up half on empty cursor, deposit 1 on filled.
	private void ApplyRightClick(Item[] slots)
	{
		ref Item slot = ref slots[_index];
		if (slot.IsAir && _cursor.IsAir) return;
		if (_cursor.IsAir)
		{
			// Pick up rounded-up half from slot.
			int take = (slot.stack + 1) / 2;
			_cursor = slot.Clone();
			_cursor.stack = take;
			slot.stack -= take;
			if (slot.stack <= 0) slot = new Item();
			return;
		}
		if (slot.IsAir)
		{
			// Place 1 from cursor into empty slot.
			slot = _cursor.Clone();
			slot.stack = 1;
			_cursor.stack -= 1;
			if (_cursor.stack <= 0) _cursor = new Item();
			return;
		}
		if (CanStack(slot, _cursor) && slot.stack < slot.maxStack)
		{
			slot.stack += 1;
			_cursor.stack -= 1;
			if (_cursor.stack <= 0) _cursor = new Item();
		}
		// Different type with non-empty cursor: vanilla does nothing.
	}

	// Shift+L-click: detach the whole slot stack and ship it to the player's
	// inventory via Player.GetItem (handled client-side in CursorUpdatePacket).
	// Cursor is untouched.
	private void ApplyShiftOut(Item[] slots, int byWhoAmI)
	{
		ref Item slot = ref slots[_index];
		if (slot.IsAir) return;
		var detached = slot;
		slot = new Item();
		WriteBackCursor(byWhoAmI, detached, CursorUpdatePacket.Delivery.PlayerInventory);
	}

	// Shift+L-click in PLAYER inventory while a machine UI is open: insert the
	// source stack into the machine's accepting slots (every input slot the
	// IItemHandler exposes whose IsItemValid says yes), leftover comes back to
	// the player via CursorUpdatePacket(PlayerInventory) -> Player.GetItem.
	//
	// This is the inverse of ShiftClickOut. The reason it needs a server-
	// authoritative round-trip rather than a pure client-side Insert (the way
	// the client originally did it) is that machine slot state lives on the
	// server: a client-side Insert mutates only the local snapshot, which the
	// next periodic MachineStateSyncPacket overwrites - items leave the player
	// slot locally but never land in the machine. The MP void bug.
	private void ApplyShiftIn(MetaMachine entity, int byWhoAmI)
	{
		// Default: hand the cursor straight back if we can't insert anywhere.
		if (entity is not IItemHandler handler)
		{
			WriteBackCursor(byWhoAmI, _cursor, CursorUpdatePacket.Delivery.PlayerInventory);
			return;
		}

		// Iterate every machine slot. IsItemValid gates per-slot filters
		// (input-only on processing machines, type-specific on filtered slots).
		// Mirrors the original client-side loop in MachineShiftClickPlayer
		// before this path was server-routed.
		for (int s = 0; s < handler.SlotCount && _cursor.stack > 0; s++)
		{
			if (!handler.IsItemValid(s, _cursor)) continue;
			var leftover = handler.Insert(s, _cursor, simulate: false);
			_cursor.stack = leftover?.stack ?? 0;
			if (_cursor.stack <= 0) { _cursor.TurnToAir(); break; }
		}

		// NotifiableItemStackHandler.Insert fires its own OnContentsChanged via
		// the storage callback, so RecipeLogic re-scans without a manual
		// NotifySlotGroupChanged here (unlike the ref-write L/R paths).

		WriteBackCursor(byWhoAmI, _cursor, CursorUpdatePacket.Delivery.PlayerInventory);
	}

	private static bool CanStack(Item a, Item b) =>
		a.type == b.type && a.prefix == b.prefix && a.maxStack > 1 &&
		// Honor per-stack GlobalItem/ModItem differences (research data, future
		// NBT) - same rule the machine handler + vanilla slots use.
		Terraria.ModLoader.ItemLoader.CanStack(a, b);

	private static void WriteBackCursor(int byWhoAmI, Item result, CursorUpdatePacket.Delivery delivery)
	{
		if (Main.netMode == NetmodeID.Server)
		{
			CursorUpdatePacket.SendTo(byWhoAmI, result, delivery);
			return;
		}
		// SinglePlayer - we are the client. Apply directly.
		switch (delivery)
		{
			case CursorUpdatePacket.Delivery.Cursor:
				Main.mouseItem = result;
				break;
			case CursorUpdatePacket.Delivery.PlayerInventory:
				if (result.IsAir) return;
				var leftover = Main.LocalPlayer.GetItem(
					Main.myPlayer, result,
					Terraria.GetItemSettings.InventoryEntityToPlayerInventorySettings);
				if (!leftover.IsAir && leftover.stack > 0)
					Item.NewItem(new Terraria.DataStructures.EntitySource_Misc("gtceu_cursor_overflow"),
						Main.LocalPlayer.position, Main.LocalPlayer.width, Main.LocalPlayer.height,
						leftover.type, leftover.stack, false, leftover.prefix);
				break;
		}
	}
}
