#nullable enable
using System.Collections.Generic;
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Casings;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Net;

// Multiblock active-state edge broadcast. Payload = controller anchor +
// active flag + affected casing cells. Late-join via ActiveCasingRequest.
public static class ActiveCasingPacket
{
	public static void SendBroadcast(int controllerX, int controllerY,
	                                 bool active, IReadOnlyList<Point16> cells)
	{
		if (Main.netMode != NetmodeID.Server) return;
		var p = NetRouter.NewPacket(PacketType.ActiveCasingSet);
		WriteBody(p, controllerX, controllerY, active, cells);
		p.Send();
	}

	private static void WriteBody(BinaryWriter w, int controllerX, int controllerY,
	                              bool active, IReadOnlyList<Point16> cells)
	{
		w.Write((short)controllerX);
		w.Write((short)controllerY);
		w.Write(active);
		w.Write((ushort)cells.Count);
		foreach (var c in cells)
		{
			w.Write((short)c.X);
			w.Write((short)c.Y);
		}
	}

	public static void HandleSet(BinaryReader r)
	{
		_ = r.ReadInt16();  // controllerX - diagnostic only
		_ = r.ReadInt16();  // controllerY
		bool active = r.ReadBoolean();
		int n = r.ReadUInt16();
		var cells = new List<Point16>(n);
		for (int i = 0; i < n; i++)
		{
			short cx = r.ReadInt16();
			short cy = r.ReadInt16();
			cells.Add(new Point16(cx, cy));
		}
		if (Main.netMode != NetmodeID.MultiplayerClient) return;
		if (active) ActiveCasingState.SetActive(cells);
		else        ActiveCasingState.ClearActive(cells);
	}

	public static void SendRequest()
	{
		if (Main.netMode != NetmodeID.MultiplayerClient) return;
		var p = NetRouter.NewPacket(PacketType.ActiveCasingRequest);
		p.Send();
	}

	public static void HandleRequest(BinaryReader r, int whoAmI)
	{
		if (Main.netMode != NetmodeID.Server) return;

		foreach (var te in Terraria.DataStructures.TileEntity.ByID.Values)
		{
			if (te is not MetaMachine machine) continue;
			if (machine is not MultiblockControllerMachine controller) continue;
			if (!controller.IsFormed) continue;
			if (!controller.IsActive) continue;

			// Active-aware casings only; same filter OnActiveStateChanged uses.
			var cells = new List<Point16>();
			foreach (var (cx, cy) in controller.GetMultiblockState().GetCache())
			{
				if (cx < 0 || cy < 0 || cx >= Main.maxTilesX || cy >= Main.maxTilesY) continue;
				var tile = Main.tile[cx, cy];
				if (!tile.HasTile) continue;
				var mt = Terraria.ModLoader.TileLoader.GetTile(tile.TileType);
				if (mt is CasingTile c && c.IsActiveAware)
					cells.Add(new Point16(cx, cy));
			}
			if (cells.Count == 0) continue;

			var p = NetRouter.NewPacket(PacketType.ActiveCasingSet);
			WriteBody(p, controller.Position.X, controller.Position.Y, active: true, cells);
			p.Send(toClient: whoAmI);
		}
	}
}
