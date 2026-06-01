#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Net;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;
using Terraria;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// Super Chest interaction - the Locked / Voiding / AutoOutput toggles, the
// Dump button (extract one stack to the clicking player), and the in-GUI
// Insert slot click (deposit the player's cursor into storage). Server-
// authoritative throughout (mirrors how the Super Tank's toggles route through
// TankConfigSetAction).
//
// Op-conditional payload:
//   Locked / Voiding / AutoOutput -> bool _value
//   Dump                          -> bool _value (unused; kept for wire shape)
//   Insert                        -> Item _cursor (the player's claimed cursor)
//
// Insert delivers the leftover back via CursorUpdatePacket(Delivery.Cursor),
// which directly mutates Main.mouseItem on the originator. This bypasses
// vanilla SyncEquipment's "ignore-self" gate (MessageBuffer.cs:403) - the same
// gate that caused the dupe in the old tile-RMB-ChestInsertPacket path.
public sealed class ChestAction : IMachineAction
{
	public enum Op : byte
	{
		Locked     = 0,
		Voiding    = 1,
		AutoOutput = 2,
		Dump       = 3,   // hand one stack of the stored item to the clicking player
		Insert     = 4,   // deposit the player's cursor into storage
	}

	public PacketType Type => PacketType.ChestAction;

	private Op _op;
	private bool _value;
	private Item _cursor = new();

	public ChestAction() { }
	public ChestAction(Op op, bool value) { _op = op; _value = value; }
	// Insert overload - detach from Main.mouseItem so the wire copy can't be
	// mutated post-send (same convention as SlotAction's cursor field).
	public ChestAction(Op op, Item cursor) { _op = op; _cursor = cursor.Clone(); }

	public void Write(BinaryWriter w)
	{
		w.Write((byte)_op);
		if (_op == Op.Insert) w.WriteItem(_cursor);
		else                  w.Write(_value);
	}

	public void Read(BinaryReader r)
	{
		_op = (Op)r.ReadByte();
		if (_op == Op.Insert) _cursor = r.ReadItem();
		else                  _value = r.ReadBoolean();
	}

	public void Apply(MetaMachine entity, int byWhoAmI)
	{
		if (entity is not SuperChestTileEntity chest) return;
		switch (_op)
		{
			case Op.Locked:     chest.SetLocked(_value); break;
			case Op.Voiding:    chest.IsVoiding = _value; break;
			case Op.AutoOutput: chest.IsAutoOutput = _value; break;
			case Op.Dump:
				if (byWhoAmI >= 0 && byWhoAmI < Main.maxPlayers)
					chest.DumpStackTo(Main.player[byWhoAmI]);
				break;
			case Op.Insert:
				var leftover = chest.Insert(0, _cursor, simulate: false);
				if (Main.netMode == NetmodeID.Server)
					CursorUpdatePacket.SendTo(byWhoAmI, leftover, CursorUpdatePacket.Delivery.Cursor);
				else
					Main.mouseItem = leftover;
				break;
		}
	}
}
