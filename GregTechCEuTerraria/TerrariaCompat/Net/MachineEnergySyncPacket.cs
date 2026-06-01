#nullable enable
using System.Collections.Generic;
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria;
using Terraria.DataStructures;

namespace GregTechCEuTerraria.TerrariaCompat.Net;

// Compact per-machine energy-stored sync. Payload = (Point16 position, long
// energy) ~ 12 bytes.
//
// Energy is OMITTED from the full MachineStateSync blob
// (NotifiableEnergyContainer.SaveForSync writes nothing; WorkableElectric
// MultiblockMachine.SaveDataForSync strips `wemm_des`) because a buffer that
// drains/fills every tick would otherwise re-serialize + re-broadcast the whole
// machine blob each period, defeating the byte-equality dirty-skip - it was the
// single largest live-sync cost (energy hatches ~14 KB/s). This channel carries
// the value EXACTLY (no quantization), per-field, mirroring upstream's
// `@SyncToClient energyStored` (LDLib managed-sync) without porting the whole
// managed-sync framework.
//
// Dirty-skip is a free long-compare (machine.LastBroadcastEnergy), checked
// BEFORE gathering recipients, so an idle machine (energy unchanged) costs
// nothing. View-begin seeds a fresh GUI viewer via SendTo (the blob bootstrap
// no longer carries energy).
public static class MachineEnergySyncPacket
{
	// Reused recipient set - the broadcast loop is single-threaded, so one static
	// scratch avoids a per-machine HashSet alloc (mirrors MachineStateSyncPacket).
	private static readonly HashSet<int> _recipientScratch = new();

	// Send the current energy to ONE client (view-begin bootstrap). Does NOT
	// touch LastBroadcastEnergy so the periodic dirty-skip isn't starved.
	public static void SendTo(MetaMachine machine, int toClient)
	{
		if (Main.netMode != Terraria.ID.NetmodeID.Server) return;
		if (!machine.HasSyncEnergy) return;
		Send(machine.Position, machine.SyncEnergyStored, toClient);
	}

	// Periodic broadcast: dirty-skip on the long value FIRST (free), then send to
	// viewers union nearby players only when the value actually changed.
	public static void BroadcastNearby(MetaMachine machine)
	{
		if (Main.netMode != Terraria.ID.NetmodeID.Server) return;
		if (!machine.HasSyncEnergy) return;

		long energy = machine.SyncEnergyStored;
		string typeName = machine.GetType().Name;
		if (machine.LastBroadcastEnergy is { } prev && prev == energy)
		{
			Profiler.Profiler.Count("net.skipped", "MachineEnergySync");
			Profiler.Profiler.Count("net.energysync.skipped_by_type", typeName);
			return;
		}
		machine.LastBroadcastEnergy = energy;

		var recipients = _recipientScratch;
		recipients.Clear();
		foreach (int viewer in machine.Viewers) recipients.Add(viewer);

		float cx = machine.Position.X * 16f + machine.Size.Width * 8f;
		float cy = machine.Position.Y * 16f + machine.Size.Height * 8f;
		float radiusSq = MachineStateSyncPacket.NearbyRadiusPx * MachineStateSyncPacket.NearbyRadiusPx;
		for (int i = 0; i < Main.maxPlayers; i++)
		{
			if (recipients.Contains(i)) continue;
			var p = Main.player[i];
			if (!p.active || p.dead) continue;
			float dx = p.Center.X - cx;
			float dy = p.Center.Y - cy;
			if (dx * dx + dy * dy <= radiusSq) recipients.Add(i);
		}
		if (recipients.Count == 0) return;

		Profiler.Profiler.Count("net.energysync.sent_by_type", typeName);
		Profiler.Profiler.Count("net.energysync.bytes_by_type", typeName, 12);
		foreach (int r in recipients)
			Send(machine.Position, energy, r);
	}

	private static void Send(Point16 pos, long energy, int toClient)
	{
		var p = NetRouter.NewPacket(PacketType.MachineEnergySync);
		p.WritePoint16(pos);
		p.Write(energy);
		p.Send(toClient: toClient);
	}

	public static void HandleOnClient(BinaryReader r)
	{
		if (Main.netMode != Terraria.ID.NetmodeID.MultiplayerClient) return;
		var pos = r.ReadPoint16();
		long energy = r.ReadInt64();
		if (TileEntity.ByPosition.TryGetValue(pos, out var te) && te is MetaMachine machine)
			machine.ApplySyncEnergy(energy);
		// No bad-packet log on miss: an energy packet can briefly arrive before the
		// entity is created on a joining client; it's harmless to drop (the next
		// one lands once TileEntitySharing creates the machine).
	}
}
