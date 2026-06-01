#nullable enable
using System.IO;
using GregTechCEuTerraria.Api.Cover;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// Place or remove a cover on one side of a machine. Server-authoritative - the
// client sends only its intent (side + cover id + the cover item it claims to
// hold); the server resolves the CoverDefinition from CoverRegistry and mutates
// the machine's cover container. The post-apply MachineStateSyncPacket
// broadcast (done by MachineActions.HandleIncoming) carries the new cover state
// to every viewer, since SaveData now serializes covers.
//
// A removed cover's item is handed back onto the acting player's cursor
// (server-confirmed via WriteBackCursor, same as a successful place).
public sealed class CoverAction : ICoverAction
{
	public enum Kind : byte
	{
		Place  = 0,
		Remove = 1,
	}

	public PacketType Type => PacketType.CoverAction;

	private Kind _kind;
	private CoverSide _side;
	private string _coverId = "";
	private Item _coverItem = new();

	public CoverAction() { }

	public static CoverAction Place(CoverSide side, string coverId, Item coverItem) =>
		new() { _kind = Kind.Place, _side = side, _coverId = coverId, _coverItem = coverItem.Clone() };

	public static CoverAction Remove(CoverSide side) =>
		new() { _kind = Kind.Remove, _side = side };

	public void Write(BinaryWriter w)
	{
		w.Write((byte)_kind);
		w.Write((byte)_side);
		if (_kind == Kind.Place)
		{
			w.Write(_coverId);
			w.WriteItem(_coverItem);
		}
	}

	public void Read(BinaryReader r)
	{
		_kind = (Kind)r.ReadByte();
		_side = (CoverSide)r.ReadByte();
		if (_kind == Kind.Place)
		{
			_coverId = r.ReadString();
			_coverItem = r.ReadItem();
		}
	}

	public void Apply(ICoverable target, int byWhoAmI)
	{
		if (_kind == Kind.Place)
		{
			var definition = CoverRegistry.Get(_coverId);
			if (definition == null) return;
			// Server-authoritative validation lives in PlaceCoverOnSide
			// (CanPlaceCoverOnSide + cover.CanAttach). The cursor item is only
			// consumed if the place actually succeeds - a rejected place leaves
			// the player holding the cover, so no item is ever lost.
			if (!target.PlaceCoverOnSide(_side, _coverItem, definition))
				return;
			var remaining = _coverItem.Clone();
			if (--remaining.stack <= 0) remaining.TurnToAir();
			WriteBackCursor(byWhoAmI, remaining);
		}
		else
		{
			var drops = target.RemoveCover(_side);
			if (drops.Count == 0) return;
			// drops[0] is the cover item - hand it to the acting player's
			// cursor (standard "take from slot" behaviour; the GUI only allows
			// removal with an empty cursor). Any further drops - rare, e.g. a
			// filter installed in the cover - fall to the world.
			WriteBackCursor(byWhoAmI, drops[0]);
			var pos = target.GetBlockPos();
			var src = new EntitySource_TileBreak(pos.X, pos.Y);
			for (int i = 1; i < drops.Count; i++)
			{
				if (drops[i].IsAir) continue;
				Item.NewItem(src, pos.X * 16, pos.Y * 16, 16, 16,
					drops[i].type, drops[i].stack);
			}
		}
	}

	// Deliver the post-place cursor back to the acting player - server sends a
	// CursorUpdatePacket, SP writes Main.mouseItem directly (same dance as
	// SlotAction). Consumption is therefore server-confirmed, never optimistic.
	private static void WriteBackCursor(int byWhoAmI, Item cursor)
	{
		if (Main.netMode == NetmodeID.Server)
			CursorUpdatePacket.SendTo(byWhoAmI, cursor, CursorUpdatePacket.Delivery.Cursor);
		else
			Main.mouseItem = cursor;
	}
}
