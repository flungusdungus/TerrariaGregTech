#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// Unified SP/MP action dispatcher. Every UI widget that mutates machine state
// goes through Send - no widget touches MetaMachine fields directly.
//
// Send branches once on netmode:
//   - SinglePlayer / Server: call Apply in-process. Zero packet overhead in SP.
//   - MultiplayerClient: serialize and ship to server. The server's NetRouter
//     dispatches back through HandleIncoming<T> -> Apply on the authoritative
//     entity, then broadcasts the resulting state to viewers.
//
// Because SP and MP both terminate in the same Apply call, the logic path is
// one. There is no SP fast path that could drift from the MP path.
//
// Wire layout written by Send (after NewPacket has stamped the PacketType byte):
//
//     Point16 entityPosition
//     ...action-specific payload (action.Write)...
//
// HandleIncoming reads the layout in the same order.
public static class MachineActions
{
	public static void Send(IMachineAction action, MetaMachine entity)
	{
		// Server side (incl. SP) - apply in-process and bypass the wire.
		if (Main.netMode != NetmodeID.MultiplayerClient)
		{
			action.Apply(entity, Main.myPlayer);
			// Real MP server pushes the new state to all viewers immediately
			// rather than waiting for the next throttled tick - keeps
			// perceived latency low after a click.
			if (Main.netMode == NetmodeID.Server)
				MachineStateSyncPacket.Broadcast(entity);
			return;
		}

		// Multiplayer client - ship to server.
		var p = NetRouter.NewPacket(action.Type);
		p.WritePoint16(entity.Position);
		action.Write(p);
		p.Send();
	}

	// Server-side packet entry point. NetRouter wires one case per action
	// PacketType to a call like `HandleIncoming<PowerToggleAction>(r, whoAmI)`.
	// Centralized so the netmode guard, entity resolution, and viewer-set
	// authority check don't have to be duplicated per action class.
	public static void HandleIncoming<T>(BinaryReader r, int whoAmI) where T : IMachineAction, new()
	{
		if (Main.netMode != NetmodeID.Server)
		{
			NetHelpers.LogBadPacket("action", $"{typeof(T).Name} received on non-server side");
			return;
		}
		var pos = r.ReadPoint16();
		var action = new T();
		action.Read(r);

		if (!TileEntity.ByPosition.TryGetValue(pos, out var te) || te is not MetaMachine machine)
		{
			NetHelpers.LogBadPacket("action", $"{typeof(T).Name}: no MetaMachine at {pos} from player {whoAmI}");
			return;
		}
		// Viewer-set check: a player must have the GUI open to act on the
		// machine. Prevents off-screen / out-of-range action injection from a
		// hacked client.
		if (!machine.HasViewer(whoAmI))
		{
			NetHelpers.LogBadPacket("action", $"{typeof(T).Name}: player {whoAmI} not in viewer set for {pos}");
			return;
		}
		action.Apply(machine, whoAmI);
		MachineStateSyncPacket.Broadcast(machine);
	}
}
