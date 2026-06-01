#nullable enable
using System;
using System.IO;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Pipelike;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Fluid;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.ItemPipe;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// Cover-action dispatcher. Parallel to MachineActions but routes cover-
// targeted actions (CoverAction, CoverConfigAction, CoverFilterAction)
// against any ICoverable holder - MetaMachine OR PipeCoverable - so the
// same action class drives machine-side covers AND pipe-side covers.
//
// Wire layout written by Send (after NewPacket has stamped PacketType):
//
//     byte target_kind  (0 = machine, 1 = pipe)
//     if machine: Point16 entityPosition
//     if pipe:    byte layer + short x + short y
//     ...action payload (action.Write)...
//
// HandleIncoming reads the same layout, resolves the ICoverable, applies.
public static class CoverActions
{
	public static void Send(ICoverAction action, ICoverable target)
	{
		// Server side (incl. SP): apply in-process.
		if (Main.netMode != NetmodeID.MultiplayerClient)
		{
			action.Apply(target, Main.myPlayer);
			// On a real MP server, push fresh state to all clients so every
			// view of the target reflects the change without waiting for the
			// next throttled tick.
			if (Main.netMode == NetmodeID.Server) BroadcastPostApply(target);
			return;
		}

		// MP client: ship to server.
		var p = NetRouter.NewPacket(action.Type);
		WriteTarget(p, target);
		action.Write(p);
		p.Send();
	}

	public static void HandleIncoming<T>(BinaryReader r, int whoAmI) where T : ICoverAction, new()
	{
		if (Main.netMode != NetmodeID.Server)
		{
			NetHelpers.LogBadPacket("cover-action", $"{typeof(T).Name} received on non-server side");
			return;
		}

		var target = ResolveTarget(r, out string targetDesc);
		var action = new T();
		action.Read(r);

		if (target is null)
		{
			NetHelpers.LogBadPacket("cover-action",
				$"{typeof(T).Name}: target not found ({targetDesc}) from player {whoAmI}");
			return;
		}

		// Viewer-set authority check - only for machine targets. Pipe targets
		// don't track viewers per-cell today; that lands with the per-pipe
		// settings UI's view-begin packet. Until then, pipe-target actions
		// trust the caller (same posture as cable layer mutations).
		if (target is MetaMachine machine)
		{
			if (!machine.HasViewer(whoAmI))
			{
				NetHelpers.LogBadPacket("cover-action",
					$"{typeof(T).Name}: player {whoAmI} not in viewer set for {targetDesc}");
				return;
			}
		}

		action.Apply(target, whoAmI);
		BroadcastPostApply(target);
	}

	// Per-target-kind state broadcast after a successful apply.
	private static void BroadcastPostApply(ICoverable target)
	{
		switch (target)
		{
			case MetaMachine machine:
				MachineStateSyncPacket.Broadcast(machine);
				break;
			case PipeCoverable pipe:
				PipeCoverSyncPacket.Broadcast(pipe.Layer, pipe.X, pipe.Y);
				// If the cell now has zero covers (last one was removed),
				// drop the server-side PipeCoverable too so the dict stays
				// sparse. The broadcast already told clients to do the same.
				if (!((ICoverable)pipe).HasAnyCover())
				{
					if (pipe.Layer == PipeKind.Fluid) FluidPipeLayerSystem.DropSides(pipe.X, pipe.Y);
					else                              ItemPipeLayerSystem .DropSides(pipe.X, pipe.Y);
				}
				break;
		}
	}

	// -- Wire helpers -----------------------------------------------------
	private static void WriteTarget(BinaryWriter w, ICoverable target)
	{
		switch (target)
		{
			case MetaMachine machine:
				w.Write((byte)0);
				w.WritePoint16(machine.Position);
				break;
			case PipeCoverable pipe:
				w.Write((byte)1);
				w.Write((byte)pipe.Layer);
				w.Write((short)pipe.X);
				w.Write((short)pipe.Y);
				break;
			default:
				throw new InvalidOperationException(
					$"CoverActions.Send: unknown ICoverable type {target.GetType().Name}");
		}
	}

	private static ICoverable? ResolveTarget(BinaryReader r, out string desc)
	{
		byte kind = r.ReadByte();
		if (kind == 0)
		{
			var pos = r.ReadPoint16();
			desc = $"machine@{pos}";
			return TileEntity.ByPosition.TryGetValue(pos, out var te) && te is MetaMachine m
				? m
				: null;
		}
		if (kind == 1)
		{
			var layer = (PipeKind)r.ReadByte();
			int x = r.ReadInt16();
			int y = r.ReadInt16();
			desc = $"pipe[{layer}]@({x},{y})";
			// Pipe cell must exist at this coord. The PipeCoverable itself
			// is lazily-created via EnsureSides - a cover Place needs an
			// instance even if no covers were attached before.
			bool cellExists = layer == PipeKind.Fluid
				? FluidPipeLayerSystem.Pipes.Has(x, y)
				: ItemPipeLayerSystem .Pipes.Has(x, y);
			if (!cellExists) return null;
			return layer == PipeKind.Fluid
				? FluidPipeLayerSystem.EnsureSides(x, y)
				: ItemPipeLayerSystem .EnsureSides(x, y);
		}
		desc = $"unknown(kind={kind})";
		return null;
	}
}
