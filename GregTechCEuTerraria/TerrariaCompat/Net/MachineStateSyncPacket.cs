#nullable enable
using System.Collections.Generic;
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Net;

// Full state snapshot of a single machine. Payload = (Point16 position, full
// SaveData TagCompound). Sent on view-begin, periodically by EnergyNetSystem,
// and after mutations not covered by delta packets.
public static class MachineStateSyncPacket
{
	// Matches vanilla LegacySoundPlayer.SoundAttenuationDistance (~156 tiles).
	public const float NearbyRadiusPx = 2500f;
	private const float NearbyRadiusPxSq = NearbyRadiusPx * NearbyRadiusPx;

	public static void SendTo(MetaMachine machine, int toClient)
	{
		if (Main.netMode != Terraria.ID.NetmodeID.Server) return;
		byte[] blob = SerializeOnce(machine);
		SendBlobTo(machine.Position, blob, toClient);
	}

	public static void Broadcast(MetaMachine machine)
	{
		if (Main.netMode != Terraria.ID.NetmodeID.Server) return;
		machine.LastBroadcastBlob = null;
		if (machine.ViewerCount == 0) return;
		byte[] blob = SerializeOnce(machine);
		foreach (int viewer in machine.Viewers)
			SendBlobTo(machine.Position, blob, viewer);
	}

	// Reused recipient set - PostUpdateWorld's broadcast loop is single-threaded,
	// so one static scratch avoids a per-machine HashSet allocation (was a real
	// contributor to the broadcast-tick alloc churn on a large base).
	private static readonly HashSet<int> _recipientScratch = new();

	// Snapshot once, send to viewers + players within NearbyRadiusPx of the
	// footprint center. Dirty-skip via byte-equality against the last broadcast
	// so idle machines emit zero traffic. View-begin invalidates the cache so
	// fresh viewers always get a sync (see InvalidateBroadcast).
	public static void BroadcastNearby(MetaMachine machine)
	{
		if (Main.netMode != Terraria.ID.NetmodeID.Server) return;

		// Gather recipients (viewers union players within radius) BEFORE serializing.
		// If nobody is listening, skip the SaveDataForSync serialize entirely - a
		// broadcast with no recipient sends nothing, and a player who later walks
		// into range is bootstrapped by TileEntitySharing (chunk-load) + the
		// resumed broadcast. On a large base most machines are far from every
		// player at any tick, so they were paying a full NBT serialize purely to
		// feed the dirty-compare for a send that never fired. This is the dominant
		// cost in the state-broadcast phase. Behaviour is unchanged: the SEND set
		// was already gated to viewers+nearby; only the dead-weight serialize goes.
		var recipients = _recipientScratch;
		recipients.Clear();
		foreach (int viewer in machine.Viewers) recipients.Add(viewer);

		float cx = machine.Position.X * 16f + machine.Size.Width * 8f;
		float cy = machine.Position.Y * 16f + machine.Size.Height * 8f;
		for (int i = 0; i < Main.maxPlayers; i++)
		{
			if (recipients.Contains(i)) continue;
			var p = Main.player[i];
			if (!p.active || p.dead) continue;
			float dx = p.Center.X - cx;
			float dy = p.Center.Y - cy;
			if (dx * dx + dy * dy <= NearbyRadiusPxSq) recipients.Add(i);
		}
		if (recipients.Count == 0) return;

		byte[] blob = SerializeOnce(machine);
		string typeName = machine.GetType().Name;

		if (machine.LastBroadcastBlob is { } prev && BlobEquals(prev, blob))
		{
			Profiler.Profiler.Count("net.skipped", "MachineStateSync");
			Profiler.Profiler.Count("net.sync.skipped_by_type", typeName);
			return;
		}
		machine.LastBroadcastBlob = blob;

		// Per-type attribution so a JSON dump can name which machine kind is
		// dominating the live (non-skipped) broadcast traffic. `sent_by_type`
		// counts events, `bytes_by_type` sums payload bytes (pre-recipient
		// fan-out - the same blob is reused across viewers + nearby players).
		Profiler.Profiler.Count("net.sync.sent_by_type", typeName);
		Profiler.Profiler.Count("net.sync.bytes_by_type", typeName, blob.Length);

		foreach (int r in recipients)
			SendBlobTo(machine.Position, blob, r);
	}

	// Returned bytes are the wire payload AFTER the position (per-Send) - i.e.
	// just the NBT body. SendBlobTo prepends position.
	//
	// SaveDataForSync (not SaveData) is the wire-only snapshot: by default it
	// mirrors SaveData, but a noisy machine/trait can override to omit per-tick
	// monotonic fields (recipe progress, scan cursors) so byte-equality
	// dirty-skip actually fires between status transitions. Disk save still
	// goes through SaveData and is unaffected.
	private static byte[] SerializeOnce(MetaMachine machine)
	{
		var tag = new TagCompound();
		machine.SaveDataForSync(tag);
		using var ms = new MemoryStream();
		using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
			TagIO.Write(tag, bw);
		return ms.ToArray();
	}

	private static void SendBlobTo(Point16 pos, byte[] blob, int toClient)
	{
		var p = NetRouter.NewPacket(PacketType.MachineStateSync);
		p.WritePoint16(pos);
		p.Write(blob);  // no length prefix; TagIO.Read parses the body
		p.Send(toClient: toClient);
	}

	private static bool BlobEquals(byte[] a, byte[] b)
	{
		if (a.Length != b.Length) return false;
		for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
		return true;
	}

	// Drop the broadcast cache so the next BroadcastNearby sends fresh.
	// Called on viewer-join so the new GUI sees real state immediately.
	public static void InvalidateBroadcast(MetaMachine machine)
	{
		machine.LastBroadcastBlob = null;
	}

	public static void HandleOnClient(BinaryReader r)
	{
		if (Main.netMode != Terraria.ID.NetmodeID.MultiplayerClient) return;
		var pos = r.ReadPoint16();
		var tag = TagIO.Read(r);
		if (TileEntity.ByPosition.TryGetValue(pos, out var te) && te is MetaMachine machine)
		{
			machine.LoadData(tag);
			machine.OnClientSync();
		}
		else
		{
			NetHelpers.LogBadPacket("StateSync", $"no MetaMachine at {pos} on client; sync dropped");
		}
	}
}
