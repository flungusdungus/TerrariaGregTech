#nullable enable
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Fluid;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.ItemPipe;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Laser;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike;

// Bridges Forge's `neighborChanged` event (no Terraria native) so a pipe
// whose cover was configured BEFORE its target inventory existed gets a
// chance to re-evaluate its subscription. Pipe-to-pipe changes are covered
// by *PipeLayerHandle.NotifyAdjacentCoversNeighborChanged; this fills in
// for machines, vanilla chests, and anything else placed/removed.
public sealed class PipeNeighborWatcher : GlobalTile
{
	public override void PlaceInWorld(int i, int j, int type, Item item) => NotifyAround(i, j);

	public override void KillTile(int i, int j, int type, ref bool fail, ref bool effectOnly, ref bool noItem)
	{
		if (fail || effectOnly) return;
		NotifyAround(i, j);
	}

	// MetaMachineTile.PlaceEntity invokes this directly because its
	// MachinePlacedPacket.Handle (MP server) path bypasses GlobalTile.PlaceInWorld.
	// 5x5 (not cardinal) because PlaceInWorld fires ONCE at the cursor cell
	// for a multi-tile placement; a pipe cardinal-adjacent to a NON-cursor
	// sub-cell would miss a cardinal walk. PingPipe is idempotent.
	public static void NotifyAround(int x, int y)
	{
		if (Main.netMode == NetmodeID.MultiplayerClient) return;
		for (int dy = -2; dy <= 2; dy++)
		for (int dx = -2; dx <= 2; dx++)
		{
			if (dx == 0 && dy == 0) continue;
			PingPipe(x + dx, y + dy);
		}
	}

	private static void PingPipe(int x, int y)
	{
		if (x < 0 || y < 0 || x >= Main.maxTilesX || y >= Main.maxTilesY) return;
		if (ItemPipeLayerSystem.GetSides(x, y) is { } itemPcv)
			((ICoverable)itemPcv).OnCoversNeighborChanged();
		if (FluidPipeLayerSystem.GetSides(x, y) is { } fluidPcv)
			((ICoverable)fluidPcv).OnCoversNeighborChanged();
		// Laser pipes have no covers, but their route cache (LaserPipeNet.
		// _netData) goes stale when an endpoint hatch is placed or removed
		// adjacent to a pipe. OnNeighbourUpdate clears the cache so the next
		// push re-walks the line. Without this, a destroyed-then-replaced
		// endpoint hatch wouldn't be picked up until a world reload.
		if (LaserPipeLayerSystem.Pipes.Has(x, y))
			LaserPipeNetSystem.Level.GetNetFromPos((x, y))?.OnNeighbourUpdate((x, y));
		// Same route-cache invalidation for optical pipes.
		if (Optical.OpticalPipeLayerSystem.Pipes.Has(x, y))
			Optical.OpticalPipeNetSystem.Level.GetNetFromPos((x, y))?.OnNeighbourUpdate((x, y));
	}
}
