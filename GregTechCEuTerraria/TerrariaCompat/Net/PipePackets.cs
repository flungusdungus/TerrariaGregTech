#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Pipelike;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.ItemPipe;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Fluid;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Laser;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Optical;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.LongDistance;
using Terraria;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Net;

// Pipe-layer sync (item + fluid under one packet family, kind byte first).
// Authority model matches CablePackets: client mutates locally + ships,
// server applies + echoes to other clients. Late-join via PipeLayerRequest.
// Pipe cells are immutable post-place, so last-write-wins is safe.
public static class PipePackets
{
	private static void WriteItem(BinaryWriter w, ItemPipeCell c)
	{
		w.Write(c.MaterialId);
		w.Write((byte)c.Size);
		w.Write(c.Restrictive);
		w.Write(c.Priority);
		w.Write(c.TransferRate);
		w.Write(c.IsSimple);
	}

	private static ItemPipeCell ReadItem(BinaryReader r)
	{
		string mat = r.ReadString();
		byte size  = r.ReadByte();
		bool restr = r.ReadBoolean();
		int prio   = r.ReadInt32();
		float rate = r.ReadSingle();
		bool simple = r.ReadBoolean();
		return new ItemPipeCell(mat, (PipeSize)size, restr, prio, rate, simple);
	}

	private static void WriteFluid(BinaryWriter w, FluidPipeCell c)
	{
		w.Write(c.MaterialId);
		w.Write((byte)c.Size);
		w.Write(c.Throughput);
		w.Write((byte)c.Channels);
		w.Write(c.MaxFluidTemperature);
		byte proof = 0;
		if (c.GasProof)    proof |= 1;
		if (c.CryoProof)   proof |= 2;
		if (c.PlasmaProof) proof |= 4;
		if (c.AcidProof)   proof |= 8;
		w.Write(proof);
		w.Write(c.IsSimple);
	}

	private static FluidPipeCell ReadFluid(BinaryReader r)
	{
		string mat = r.ReadString();
		byte size  = r.ReadByte();
		int tput   = r.ReadInt32();
		byte chan  = r.ReadByte();
		int mtemp  = r.ReadInt32();
		byte proof = r.ReadByte();
		bool simple = r.ReadBoolean();
		return new FluidPipeCell(mat, (PipeSize)size, tput, chan, mtemp,
			GasProof:    (proof & 1) != 0,
			CryoProof:   (proof & 2) != 0,
			PlasmaProof: (proof & 4) != 0,
			AcidProof:   (proof & 8) != 0,
			IsSimple:    simple);
	}

	public static void SendPlacedItem(int x, int y, ItemPipeCell cell)
	{
		if (Main.netMode == NetmodeID.SinglePlayer) return;
		var p = NetRouter.NewPacket(PacketType.PipePlaced);
		p.Write((byte)PipeKind.Item);
		p.Write((short)x);
		p.Write((short)y);
		WriteItem(p, cell);
		p.Send();
	}

	public static void SendPlacedFluid(int x, int y, FluidPipeCell cell)
	{
		if (Main.netMode == NetmodeID.SinglePlayer) return;
		var p = NetRouter.NewPacket(PacketType.PipePlaced);
		p.Write((byte)PipeKind.Fluid);
		p.Write((short)x);
		p.Write((short)y);
		WriteFluid(p, cell);
		p.Send();
	}

	// Laser cells have zero payload (single NORMAL variant upstream) - only
	// the kind byte + coords ship.
	public static void SendPlacedLaser(int x, int y)
	{
		if (Main.netMode == NetmodeID.SinglePlayer) return;
		var p = NetRouter.NewPacket(PacketType.PipePlaced);
		p.Write((byte)PipeKind.Laser);
		p.Write((short)x);
		p.Write((short)y);
		p.Send();
	}

	// Optical cells have zero payload (single NORMAL variant upstream) - only
	// the kind byte + coords ship.
	public static void SendPlacedOptical(int x, int y)
	{
		if (Main.netMode == NetmodeID.SinglePlayer) return;
		var p = NetRouter.NewPacket(PacketType.PipePlaced);
		p.Write((byte)PipeKind.Optical);
		p.Write((short)x);
		p.Write((short)y);
		p.Send();
	}

	// LD cells carry a single Type byte (item / fluid). No open-mask: LD pipes
	// bend/branch freely, connectivity is implicit same-type adjacency.
	public static void SendPlacedLongDistance(int x, int y, LongDistancePipeType type)
	{
		if (Main.netMode == NetmodeID.SinglePlayer) return;
		var p = NetRouter.NewPacket(PacketType.PipePlaced);
		p.Write((byte)PipeKind.LongDistance);
		p.Write((short)x);
		p.Write((short)y);
		p.Write((byte)type);
		p.Send();
	}

	public static void HandlePlaced(BinaryReader r, int whoAmI)
	{
		var kind = (PipeKind)r.ReadByte();
		int x = r.ReadInt16();
		int y = r.ReadInt16();

		if (kind == PipeKind.LongDistance)
		{
			var type = (LongDistancePipeType)r.ReadByte();
			if (Main.netMode == NetmodeID.Server)
			{
				LongDistancePipeLayerSystem.Pipes.Set(x, y, new LongDistancePipeCell(type));
				LongDistancePipeNetSystem.OnPipeAdded(x, y);
				var p = NetRouter.NewPacket(PacketType.PipePlaced);
				p.Write((byte)PipeKind.LongDistance);
				p.Write((short)x); p.Write((short)y); p.Write((byte)type);
				p.Send(ignoreClient: whoAmI);
				return;
			}
			if (Main.netMode == NetmodeID.MultiplayerClient)
				LongDistancePipeLayerSystem.Pipes.Set(x, y, new LongDistancePipeCell(type));
			return;
		}

		if (kind == PipeKind.Optical)
		{
			// ConnectOnPlace recomputes the <=2 reciprocal connections from the
			// (synced) layer state - deterministic, so server + every client
			// converge to the same open-masks without shipping them per-place.
			if (Main.netMode == NetmodeID.Server)
			{
				OpticalConn.ConnectOnPlace(OpticalPipeLayerSystem.Pipes, x, y);
				OpticalPipeNetSystem.OnPipeAdded(x, y);
				var p = NetRouter.NewPacket(PacketType.PipePlaced);
				p.Write((byte)PipeKind.Optical);
				p.Write((short)x); p.Write((short)y);
				p.Send(ignoreClient: whoAmI);
				return;
			}
			if (Main.netMode == NetmodeID.MultiplayerClient)
				OpticalConn.ConnectOnPlace(OpticalPipeLayerSystem.Pipes, x, y);
			return;
		}

		if (kind == PipeKind.Laser)
		{
			// ConnectOnPlace recomputes the straight-only connections from the
			// (synced) layer - deterministic, so server + clients converge.
			if (Main.netMode == NetmodeID.Server)
			{
				LaserConn.ConnectOnPlace(LaserPipeLayerSystem.Pipes, x, y);
				LaserPipeNetSystem.OnPipeAdded(x, y);
				var p = NetRouter.NewPacket(PacketType.PipePlaced);
				p.Write((byte)PipeKind.Laser);
				p.Write((short)x); p.Write((short)y);
				p.Send(ignoreClient: whoAmI);
				return;
			}
			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				LaserConn.ConnectOnPlace(LaserPipeLayerSystem.Pipes, x, y);
				// Net rebuild fires on PostUpdateEverything when IsDirty is set.
			}
			return;
		}

		if (kind == PipeKind.Fluid)
		{
			var cell = ReadFluid(r);
			if (Main.netMode == NetmodeID.Server)
			{
				FluidPipeLayerSystem.Pipes.Set(x, y, cell);
				Pipelike.Fluid.FluidPipeLayerHandle.NotifyAdjacentCoversNeighborChanged(x, y);
				var p = NetRouter.NewPacket(PacketType.PipePlaced);
				p.Write((byte)PipeKind.Fluid);
				p.Write((short)x); p.Write((short)y); WriteFluid(p, cell);
				p.Send(ignoreClient: whoAmI);
				return;
			}
			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				FluidPipeLayerSystem.Pipes.Set(x, y, cell);
				Pipelike.Fluid.FluidPipeLayerHandle.NotifyAdjacentCoversNeighborChanged(x, y);
			}
		}
		else
		{
			var cell = ReadItem(r);
			if (Main.netMode == NetmodeID.Server)
			{
				ItemPipeLayerSystem.Pipes.Set(x, y, cell);
				Pipelike.ItemPipe.ItemPipeLayerHandle.NotifyAdjacentCoversNeighborChanged(x, y);
				var p = NetRouter.NewPacket(PacketType.PipePlaced);
				p.Write((byte)PipeKind.Item);
				p.Write((short)x); p.Write((short)y); WriteItem(p, cell);
				p.Send(ignoreClient: whoAmI);
				return;
			}
			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				ItemPipeLayerSystem.Pipes.Set(x, y, cell);
				Pipelike.ItemPipe.ItemPipeLayerHandle.NotifyAdjacentCoversNeighborChanged(x, y);
			}
		}
	}

	public static void SendRemove(int x, int y, PipeKind kind)
	{
		if (Main.netMode == NetmodeID.SinglePlayer) return;
		var p = NetRouter.NewPacket(PacketType.PipeRemove);
		p.Write((byte)kind);
		p.Write((short)x);
		p.Write((short)y);
		p.Send();
	}

	public static void HandleRemove(BinaryReader r, int whoAmI)
	{
		var kind = (PipeKind)r.ReadByte();
		int x = r.ReadInt16();
		int y = r.ReadInt16();

		if (kind == PipeKind.LongDistance)
		{
			if (Main.netMode == NetmodeID.Server)
			{
				LongDistancePipeLayerSystem.Pipes.Remove(x, y);
				LongDistancePipeNetSystem.OnPipeRemoved(x, y);
				var p = NetRouter.NewPacket(PacketType.PipeRemove);
				p.Write((byte)kind);
				p.Write((short)x); p.Write((short)y);
				p.Send(ignoreClient: whoAmI);
				return;
			}
			if (Main.netMode == NetmodeID.MultiplayerClient)
				LongDistancePipeLayerSystem.Pipes.Remove(x, y);
			return;
		}

		if (kind == PipeKind.Optical)
		{
			if (Main.netMode == NetmodeID.Server)
			{
				OpticalPipeLayerSystem.Pipes.Remove(x, y);
				OpticalConn.ClearOnRemove(OpticalPipeLayerSystem.Pipes, x, y);
				OpticalPipeNetSystem.OnPipeRemoved(x, y);
				var p = NetRouter.NewPacket(PacketType.PipeRemove);
				p.Write((byte)kind);
				p.Write((short)x); p.Write((short)y);
				p.Send(ignoreClient: whoAmI);
				return;
			}
			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				OpticalPipeLayerSystem.Pipes.Remove(x, y);
				OpticalConn.ClearOnRemove(OpticalPipeLayerSystem.Pipes, x, y);
			}
			return;
		}

		if (kind == PipeKind.Laser)
		{
			if (Main.netMode == NetmodeID.Server)
			{
				LaserPipeLayerSystem.Pipes.Remove(x, y);
				LaserConn.ClearOnRemove(LaserPipeLayerSystem.Pipes, x, y);
				LaserPipeNetSystem.OnPipeRemoved(x, y);
				var p = NetRouter.NewPacket(PacketType.PipeRemove);
				p.Write((byte)kind);
				p.Write((short)x); p.Write((short)y);
				p.Send(ignoreClient: whoAmI);
				return;
			}
			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				LaserPipeLayerSystem.Pipes.Remove(x, y);
				LaserConn.ClearOnRemove(LaserPipeLayerSystem.Pipes, x, y);
			}
			return;
		}

		bool isFluid = kind == PipeKind.Fluid;
		if (Main.netMode == NetmodeID.Server)
		{
			if (isFluid) { FluidPipeLayerSystem.Pipes.Remove(x, y); FluidPipeLayerSystem.DropSides(x, y); }
			else         { ItemPipeLayerSystem .Pipes.Remove(x, y); ItemPipeLayerSystem .DropSides(x, y); }
			NotifyAdjacent(isFluid, x, y);
			var p = NetRouter.NewPacket(PacketType.PipeRemove);
			p.Write((byte)kind);
			p.Write((short)x); p.Write((short)y);
			p.Send(ignoreClient: whoAmI);
			return;
		}
		if (Main.netMode == NetmodeID.MultiplayerClient)
		{
			if (isFluid) { FluidPipeLayerSystem.Pipes.Remove(x, y); FluidPipeLayerSystem.DropSides(x, y); }
			else         { ItemPipeLayerSystem .Pipes.Remove(x, y); ItemPipeLayerSystem .DropSides(x, y); }
			NotifyAdjacent(isFluid, x, y);
		}
	}

	private static void NotifyAdjacent(bool isFluid, int x, int y)
	{
		if (isFluid) Pipelike.Fluid.FluidPipeLayerHandle.NotifyAdjacentCoversNeighborChanged(x, y);
		else         Pipelike.ItemPipe.ItemPipeLayerHandle.NotifyAdjacentCoversNeighborChanged(x, y);
	}

	public static void SendLayerRequest(PipeKind kind)
	{
		if (Main.netMode != NetmodeID.MultiplayerClient) return;
		var p = NetRouter.NewPacket(PacketType.PipeLayerRequest);
		p.Write((byte)kind);
		p.Send();
	}

	public static void HandleLayerRequest(BinaryReader r, int whoAmI)
	{
		if (Main.netMode != NetmodeID.Server) return;
		var kind = (PipeKind)r.ReadByte();
		var p = NetRouter.NewPacket(PacketType.PipeLayerFull);
		p.Write((byte)kind);
		if (kind == PipeKind.LongDistance)
		{
			var layer = LongDistancePipeLayerSystem.Pipes;
			p.Write(layer.Count);
			foreach (var kv in layer.All)
			{
				p.Write((short)kv.Key.x);
				p.Write((short)kv.Key.y);
				p.Write((byte)kv.Value.Type);
			}
			p.Send(toClient: whoAmI);
			return; // No covers on LD pipes; skip the cover sync.
		}
		if (kind == PipeKind.Optical)
		{
			var layer = OpticalPipeLayerSystem.Pipes;
			p.Write(layer.Count);
			foreach (var kv in layer.All)
			{
				p.Write((short)kv.Key.x);
				p.Write((short)kv.Key.y);
				p.Write(kv.Value.Open);   // late-joiner can't replay placement order
			}
			p.Send(toClient: whoAmI);
			return; // No covers on optical pipes; skip the cover sync.
		}
		if (kind == PipeKind.Laser)
		{
			var layer = LaserPipeLayerSystem.Pipes;
			p.Write(layer.Count);
			foreach (var kv in layer.All)
			{
				p.Write((short)kv.Key.x);
				p.Write((short)kv.Key.y);
				p.Write(kv.Value.Open);   // late-joiner can't replay placement order
			}
			p.Send(toClient: whoAmI);
			return; // No covers on laser pipes; skip the cover sync.
		}
		if (kind == PipeKind.Fluid)
		{
			var layer = FluidPipeLayerSystem.Pipes;
			p.Write(layer.Count);
			foreach (var kv in layer.All)
			{
				p.Write((short)kv.Key.x);
				p.Write((short)kv.Key.y);
				WriteFluid(p, kv.Value);
			}
		}
		else
		{
			var layer = ItemPipeLayerSystem.Pipes;
			p.Write(layer.Count);
			foreach (var kv in layer.All)
			{
				p.Write((short)kv.Key.x);
				p.Write((short)kv.Key.y);
				WriteItem(p, kv.Value);
			}
		}
		p.Send(toClient: whoAmI);

		// Late-join cover sync - one PipeCoverable blob per configured side.
		var sides = kind == PipeKind.Fluid
			? FluidPipeLayerSystem.AllSides
			: ItemPipeLayerSystem .AllSides;
		foreach (var kv in sides)
			PipeCoverSyncPacket.SendTo(kind, kv.Key.x, kv.Key.y, whoAmI);
	}

	public static void HandleLayerFull(BinaryReader r)
	{
		if (Main.netMode != NetmodeID.MultiplayerClient) return;
		var kind = (PipeKind)r.ReadByte();
		int n = r.ReadInt32();
		if (kind == PipeKind.LongDistance)
		{
			LongDistancePipeLayerSystem.Pipes.Clear();
			for (int i = 0; i < n; i++)
			{
				int x = r.ReadInt16();
				int y = r.ReadInt16();
				var type = (LongDistancePipeType)r.ReadByte();
				LongDistancePipeLayerSystem.Pipes.Set(x, y, new LongDistancePipeCell(type));
			}
			// IsDirty auto-set; net rebuild fires on next PostUpdateEverything.
			return;
		}
		if (kind == PipeKind.Optical)
		{
			OpticalPipeLayerSystem.Pipes.Clear();
			for (int i = 0; i < n; i++)
			{
				int x = r.ReadInt16();
				int y = r.ReadInt16();
				byte open = r.ReadByte();
				OpticalPipeLayerSystem.Pipes.Set(x, y, new OpticalPipeCell { Open = open });
			}
			return;
		}
		if (kind == PipeKind.Laser)
		{
			LaserPipeLayerSystem.Pipes.Clear();
			for (int i = 0; i < n; i++)
			{
				int x = r.ReadInt16();
				int y = r.ReadInt16();
				byte open = r.ReadByte();
				LaserPipeLayerSystem.Pipes.Set(x, y, new LaserPipeCell { Open = open });
			}
			// IsDirty auto-set; LaserPipeNetSystem.MaybeRebuild rebuilds the
			// client-side net on next PostUpdateEverything.
			return;
		}
		if (kind == PipeKind.Fluid)
		{
			FluidPipeLayerSystem.Pipes.Clear();
			for (int i = 0; i < n; i++)
			{
				int x = r.ReadInt16();
				int y = r.ReadInt16();
				FluidPipeLayerSystem.Pipes.Set(x, y, ReadFluid(r));
			}
		}
		else
		{
			ItemPipeLayerSystem.Pipes.Clear();
			for (int i = 0; i < n; i++)
			{
				int x = r.ReadInt16();
				int y = r.ReadInt16();
				ItemPipeLayerSystem.Pipes.Set(x, y, ReadItem(r));
			}
			// Pipes.IsDirty auto-set; net rebuild fires on next
			// PostUpdateEverything via ItemPipeNetSystem.MaybeRebuild.
		}
	}
}
